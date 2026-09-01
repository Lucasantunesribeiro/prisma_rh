using Amazon.Lambda.AspNetCoreServer.Hosting;
﻿using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using PrismaRH.Api.Endpoints;
using PrismaRH.Api.Identidade;
using PrismaRH.Api.Producao;
using PrismaRH.Api.Saude;
using PrismaRH.Api.Servicos;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Infraestrutura;
using PrismaRH.Infraestrutura.Identidade;
using PrismaRH.Infraestrutura.Integracoes;
using PrismaRH.Infraestrutura.Persistencia;

var builder = WebApplication.CreateBuilder(args);

const string PoliticaCors = "origens-permitidas";


// ------------------------------------------------------ Lambda (Fase 10)
//
// Quando `AWS_LAMBDA_FUNCTION_NAME` existe, a aplicacao roda DENTRO da Lambda e
// precisa falar o protocolo dela em vez de abrir um socket. Fora da Lambda a
// linha nao faz nada, e o Kestrel sobe normal - o mesmo binario serve os dois.
//
// `HttpApiV2` e o formato de evento da **Function URL**, que e o endpoint
// publico escolhido. Nao ha API Gateway: ele nao esta na tabela de Free Tier
// permanente da AWS e cobraria desde a primeira requisicao.
if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AWS_LAMBDA_FUNCTION_NAME")))
{
    builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);
}

builder.Services.AddOpenApi();
builder.Services.AdicionarInfraestrutura(builder.Configuration);

// Retorna erros nao tratados no formato ProblemDetails (RFC 9457), sem expor stack trace.
builder.Services.AddProblemDetails();

// Traduz falha de PROTOCOLO em 400/413, em vez de 500. Fecha a pendencia
// `CLAUDE.md 24.19 item 4`. Ver TratamentoDeErro.
builder.Services.AddExceptionHandler<TratamentoDeErro>();

// Enum vira texto no JSON. Sem isto, "perfil" sai como 3 em vez de "AnalistaRh":
// o frontend passaria a depender da ORDEM da enum, e reordena-la quebraria o
// contrato silenciosamente.
builder.Services.ConfigureHttpJsonOptions(opcoes =>
    opcoes.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IContextoUsuario, ContextoUsuarioHttp>();

var origensPermitidas = builder.Configuration
    .GetSection("Cors:OrigensPermitidas")
    .Get<string[]>() ?? [];

builder.Services.AddCors(opcoes => opcoes.AddPolicy(
    PoliticaCors,
    politica => politica
        .WithOrigins(origensPermitidas)
        .AllowAnyHeader()
        .AllowAnyMethod()
        // Sem isto o cookie httpOnly do refresh token nao trafega entre
        // localhost:5173 e localhost:5080, e a sessao morre a cada recarga.
        .AllowCredentials()));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opcoes =>
    {
        var jwt = builder.Configuration.GetSection(OpcoesJwt.Secao).Get<OpcoesJwt>() ?? new OpcoesJwt();

        opcoes.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Emissor,
            ValidateAudience = true,
            ValidAudience = jwt.Audiencia,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.ChaveAssinatura)),
            ValidateLifetime = true,
            // Padrao do .NET e 5 minutos de tolerancia: um access token de 15
            // minutos viveria 20. Zero deixa a expiracao valer o que diz.
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorizationBuilder().Adicionar();

// ----------------------------------------------------- higiene (Fase 9)
// Devolve ao orcamento global o espaco de blobs que venceram. Ver VarreduraBlobs.
builder.Services.AddHostedService<VarreduraBlobs>();

// ------------------------------------------------- integracao externa (Fase 8)

// O cache e singleton porque guardar em cache por requisicao nao guarda nada. E
// ele tem teto proprio de entradas - ver CacheConsultaCnpj.
builder.Services.AddSingleton<CacheConsultaCnpj>();
builder.Services.AddSingleton<GuardaDestino>();

builder.Services
    .AddHttpClient<ConsultaCnpjBrasilApi>(cliente =>
    {
        // O prazo tambem esta no cliente, com token proprio. Este aqui e a
        // segunda cerca: se algum caminho escapar do CancellationToken, o
        // HttpClient ainda corta.
        cliente.Timeout = ConsultaCnpjBrasilApi.Prazo;
        cliente.DefaultRequestHeaders.UserAgent.ParseAdd("PrismaRH/1.0 (+portfolio)");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        // ⚠️ Desligado de proposito, e este e o ponto do item 2 do Security Gate.
        // Seguir redirect automaticamente pula a guarda de destino em todos os
        // saltos menos o primeiro - e o primeiro e o unico que ninguem precisa
        // atacar. Os redirects sao seguidos a mao, revalidando cada um.
        AllowAutoRedirect = false,
    });

// Limite por ORGANIZACAO. Nao por IP: num escritorio de BPO todo mundo sai pelo
// mesmo endereco, e o limite por IP puniria a empresa inteira pelo uso de uma
// pessoa. CLAUDE.md secao 24.18: nenhuma organizacao pode consumir a cota de um
// servico compartilhado e deixar as outras sem.
builder.Services.AddRateLimiter(opcoes =>
{
    opcoes.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // ------------------------------------------------ autenticacao (Fase 10)
    //
    // `CLAUDE.md 24.19 item 1`: nada impedia milhares de tentativas por minuto
    // contra POST /api/autenticacao/entrar. Forca bruta e credential stuffing
    // estavam abertos. Esta e a correcao.
    //
    // ⚠️ O particionamento e POR IP, e a razao e o oposto da Fase 8.
    //
    // Na consulta de CNPJ o limite e por organizacao, porque o usuario ja esta
    // autenticado e a cota protegida e a de um servico compartilhado.
    //
    // No LOGIN nao ha usuario ainda - e exatamente isso que o atacante esta
    // tentando descobrir. Particionar por e-mail deixaria um script varrer mil
    // e-mails diferentes sem estourar limite nenhum, que e o formato do
    // credential stuffing. Por IP, o mesmo script bate no teto na 11a
    // tentativa.
    //
    // O `24.18` pede combinar IP e identidade; a identidade entra na Fase 12,
    // junto com o bloqueio progressivo por conta.
    opcoes.AddPolicy(AutenticacaoEndpoints.PoliticaLoginPorIp, contexto =>
        RateLimitPartition.GetFixedWindowLimiter(
            contexto.Connection.RemoteIpAddress?.ToString() ?? "sem-ip",
            _ => new FixedWindowRateLimiterOptions
            {
                // Dez por minuto. Uma pessoa erra a senha tres, quatro vezes;
                // dez cobrem isso com folga e nao cobrem um dicionario.
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

    // Renovar e sair sao chamadas do proprio app, e acontecem com frequencia
    // legitima maior - o access token vive 15 minutos. Teto mais alto, mas
    // ainda teto: sem ele, um loop de refresh quebrado viraria tempestade.
    opcoes.AddPolicy(AutenticacaoEndpoints.PoliticaSessaoPorIp, contexto =>
        RateLimitPartition.GetFixedWindowLimiter(
            contexto.Connection.RemoteIpAddress?.ToString() ?? "sem-ip",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

    opcoes.AddPolicy(IntegracoesEndpoints.PoliticaLimite, contexto =>
        RateLimitPartition.GetFixedWindowLimiter(
            // Sem organizacao no token nao ha o que particionar. "anonimo" e uma
            // particao unica e propositalmente apertada - falha fechada.
            contexto.User.FindFirst(GeradorJwt.ClaimOrganizacao)?.Value ?? "anonimo",
            _ => new FixedWindowRateLimiterOptions
            {
                // 20 consultas por minuto por organizacao. Cadastrar empresa e
                // ato raro e deliberado; vinte cobrem o uso humano com folga e
                // nao cobrem um script.
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

var app = builder.Build();

// Cabecalhos de seguranca ANTES de tudo: eles precisam sair mesmo nas
// respostas de erro, e um handler que retorne cedo pularia um middleware
// registrado depois.
app.UsarCabecalhosSeguranca(!app.Environment.IsDevelopment());

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors(PoliticaCors);

app.UseAuthentication();

// ⚠️ DEPOIS do UseAuthentication, e a ordem e a funcionalidade.
//
// O particionador le a organizacao de `contexto.User`. Antes da autenticacao,
// `User` ainda nao tem claim nenhuma: toda requisicao cairia na particao
// "anonimo", e o limite viraria um balde unico para o sistema inteiro - uma
// organizacao sozinha deixaria todas as outras sem consulta.
//
// Um teste pegou exatamente isso. "Existe limite" e "existe limite POR
// ORGANIZACAO" sao afirmacoes diferentes, e a primeira passava com a segunda
// quebrada.
//
// Nao limita nada por conta propria: so vale onde ha RequireRateLimiting.
app.UseRateLimiter();

app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    await SemeadorDesenvolvimento.SemearAsync(app.Services);
}

// ⚠️ Fora de Development o /health devolve apenas "saudavel" ou nao.
//
// A versao detalhada lista as verificacoes por NOME - "banco-de-dados" - e o
// item 10 do Security Gate manda que o health nao revele detalhe interno.
// Numa rota ANONIMA, isso conta a topologia para qualquer varredura: saber
// que ha um banco, e que ele responde, e informacao gratuita para quem sonda.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = app.Environment.IsDevelopment()
        ? EscritorRespostaSaude.EscreverAsync
        : EscritorRespostaSaude.EscreverMinimoAsync
}).AllowAnonymous();

app.MapearAutenticacao();
app.MapearEmpresas();
app.MapearEstabelecimentos();
app.MapearCargos();
app.MapearFuncionarios();
app.MapearContratos();
app.MapearDependentes();
app.MapearFerias();
app.MapearImportacoes();
app.MapearTrabalhos();
app.MapearAnalises();
app.MapearIntegracoes();
app.MapearInconsistencias();
app.MapearAuditoria();
app.MapearPainel();
app.MapearDecimoTerceiro();
app.MapearRescisao();
app.MapearRubricas();
app.MapearFolhas();
app.MapearTabelasInss();
app.MapearTabelasFgts();
app.MapearTabelasIrrf();

app.Run();

// Expoe a classe gerada pelos top-level statements para os testes de integracao.
public partial class Program;
