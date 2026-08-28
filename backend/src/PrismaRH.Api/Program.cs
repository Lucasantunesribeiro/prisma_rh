using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using PrismaRH.Api.Endpoints;
using PrismaRH.Api.Identidade;
using PrismaRH.Api.Saude;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Infraestrutura;
using PrismaRH.Infraestrutura.Identidade;
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

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors(PoliticaCors);

app.UseAuthentication();
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
app.MapearRubricas();
app.MapearFolhas();
app.MapearTabelasInss();
app.MapearTabelasFgts();
app.MapearTabelasIrrf();

app.Run();

// Expoe a classe gerada pelos top-level statements para os testes de integracao.
public partial class Program;
