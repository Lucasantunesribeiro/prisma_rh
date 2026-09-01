# Arquitetura

## Em uma tela

```text
                        NAVEGADOR
                            │
             access token em MEMORIA (header Authorization)
             refresh em cookie httpOnly + token anti-CSRF
                            │
                            ▼
                  React 19 + TS (Vercel)
                            │
                    cross-origin, CORS com allowlist exata
                            ▼
              AWS Lambda Function URL — ASP.NET Core
    ┌───────────────────────────────────────────────────┐
    │  CabecalhosSeguranca · CORS · RateLimiter         │
    │  Authentication · Authorization (fallback: negar) │
    │  TratamentoDeErro · GuardaCsrf                    │
    ├───────────────────────────────────────────────────┤
    │  PrismaRH.Api            rotas, politicas, limites│
    │  PrismaRH.Aplicacao      casos de uso             │
    │  PrismaRH.Dominio        regras — sem EF/ASP/AWS  │
    │  PrismaRH.Infraestrutura EF Core, integracoes, IA │
    └───────────────────────────────────────────────────┘
           │                    │                 │
           ▼                    ▼                 ▼
   Neon PostgreSQL          SQS ──▶ Lambda    generativelanguage
   (filtro global)                 Worker      .googleapis.com
                                     │        (via GuardaDestino)
                                     ▼
                              Neon PostgreSQL
```

## A regra de dependência

```text
Api ──▶ Aplicacao ──▶ Dominio
Infraestrutura ──▶ Aplicacao / Dominio
```

O domínio **não** referencia ASP.NET, EF Core nem AWS SDK. Não é aspiração: é verificável
abrindo os `using` de `PrismaRH.Dominio`.

Isso tem um efeito prático, não decorativo: as regras de folha são testáveis sem subir
nada. Os testes de cálculo — a maior parte da suíte — não tocam banco, HTTP nem rede, e
por isso rodam em segundos e não têm intermitência.

## Onde cada garantia mora

| Garantia | Onde | Por que ali |
|---|---|---|
| **Isolamento multiempresa** | `PrismaRhDbContext`, filtro global | Se estivesse nos endpoints, dependeria de ninguém esquecer. Ver [ADR 002](adr/002-isolamento-por-filtro-global.md) |
| **Nenhuma vigência sobreposta** | `ex_vigencias_sem_sobreposicao`, no PostgreSQL | Em C# seria uma verificação com corrida embutida: dois pedidos leem "não há sobreposição" e ambos gravam |
| **Dinheiro exato** | `decimal` no C#, `numeric` no banco | Ponto flutuante não representa `0,10` |
| **Arredondamento** | `Dinheiro.Arredondar`, `AwayFromZero` | O padrão do .NET é *banker's rounding*, que não é o do domínio |
| **Cálculo explicável** | `LinhaMemoriaCalculo`, persistida | Reconstruir a conta depois exigiria os parâmetros vigentes daquele dia — que mudam |
| **Histórico intocado** | Congelamento no `LancamentoFolha` | Alterar a rubrica hoje não reescreve a folha de agosto |
| **Nada executa código do usuário** | Enum em rubrica, regra e consulta | Ver [ADR 007](adr/007-rubrica-por-enum-e-nao-formula.md) |
| **Falha fechada na autorização** | `SetFallbackPolicy` | Rota esquecida devolve 401 em vez de abrir |

## As fronteiras com o mundo

Três, e todas passam pela mesma guarda.

```text
BrasilAPI (CNPJ)  ─┐
Google Gemini (IA) ─┼─▶ GuardaDestino ─▶ HttpClient
                    │
                    └── allowlist FIXA EM CODIGO (nao em appsettings)
                        https obrigatorio, sem userinfo, porta padrao
                        DNS resolvido — TODOS os IPs conferidos
                        ::ffff: desembrulhado antes de decidir
                        AllowAutoRedirect = false, revalidando cada salto
```

A allowlist é fixa em código de propósito: em `appsettings`, a única barreira de destino
vira um campo que alguém preenche com pressa. Trocar de parceiro passa a ser alteração de
código com revisão — que é exatamente o peso que a decisão tem.

E a guarda é testada **sem rede**: o resolvedor de DNS é injetado. Defesa de rede testada
contra a rede real dá uma suíte que falha no avião e passa no escritório.

## Processamento assíncrono

```text
upload ──▶ API ──▶ blob no PostgreSQL (nao S3) ──▶ SQS ──▶ Lambda Worker ──▶ PostgreSQL
```

**Blob no banco, e não no S3**, porque o S3 cobra desde o primeiro byte e está fora da
tabela de Free Tier permanente. O orçamento é **global** — 50 MB somados em toda a
aplicação, não por tenant — e reservado com `pg_advisory_xact_lock`, que é de escopo de
transação e por isso funciona atrás do PgBouncer.

O orçamento é compartilhado; os dados não. Há teste provando que a organização A vê o
espaço **ocupado** por B e nenhum dos blobs de B.

Idempotência, retentativa com teto, DLQ e correlação: cada mensagem carrega
`IdOrganizacao` explícito, porque **o job não tem requisição** — e sem requisição não há
filtro global. O worker abre o contexto a partir da mensagem e confere contra o objeto
processado.

## Custo

**US$ 0,00 previstos.** Não por otimização, mas por exclusão: serviço que cobra por
existir não entra na arquitetura.

| Fora | Por quê |
|---|---|
| S3, API Gateway | Cobram desde o primeiro uso; ausentes do Free Tier permanente |
| KMS customer-managed | US$ 1,00/mês por chave, só por existir |
| NAT Gateway | ~US$ 32/mês fixos — cinco vezes o teto do projeto |
| EC2, RDS, ECS, EKS, ALB | Cobrança por hora provisionada |
| Provisioned concurrency | Mantém execução quente por hora |

O que sobra: Lambda (franquia), SQS (franquia, com long polling de 20 s), CloudWatch com
retenção curta, Neon Free e Vercel Free.

> ⚠️ **"Impossível cobrar" não é afirmação honesta.** Free Tier não é teto de gasto:
> passar da franquia não bloqueia nada, apenas cobra. O que existe são **limites técnicos**
> que tornam a ultrapassagem improvável — memória mínima, timeout curto, concorrência
> reservada, retenção de log, teto de retentativas. Os números vivem em
> `OrcamentoSemCusto`, cada um com a conta escrita ao lado.

## O que a arquitetura deliberadamente não tem

Kubernetes · Kafka · RabbitMQ · Redis · Elasticsearch · GraphQL · gRPC · Event Sourcing ·
microserviços · service mesh · WebSockets · banco vetorial · MediatR · AutoMapper ·
repositório genérico sobre o EF Core.

Nenhum deles foi recusado por preconceito. Cada um resolve um problema que este produto
não tem — e a regra do projeto é que **tecnologia entra porque resolve problema real**, não
para demonstrar familiaridade.
