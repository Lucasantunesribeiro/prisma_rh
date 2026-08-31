using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using PrismaRH.Api.Endpoints;
using PrismaRH.Api.Identidade;
using PrismaRH.Api.Saude;
using PrismaRH.Api.Servicos;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Infraestrutura;
using PrismaRH.Infraestrutura.Identidade;
using PrismaRH.Infraestrutura.Integracoes;
using PrismaRH.Infraestrutura.Persistencia;

var builder = WebApplication.CreateBuilder(args);

const string PoliticaCors = "origens-permitidas";

builder.Services.AddOpenApi();
builder.Services.AdicionarInfraestrutura(builder.Configuration);

// Retorna erros nao tratados no formato ProblemDetails (RFC 9457), sem expor stack trace.
builder.Services.AddProblemDetails();

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

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = EscritorRespostaSaude.EscreverAsync
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
