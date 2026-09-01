# ADR 006 — Lambda Function URL, e não API Gateway

**Status:** aceita · **Data:** 2026-09 (Fase 10)

## Contexto

A API precisa de um endereço HTTPS público. O caminho canônico na AWS é API Gateway HTTP
API na frente da Lambda — e era o que o roadmap previa desde a Fase 0.

## Decisão

**Lambda Function URL.** Um endereço HTTPS ligado direto à função, sem serviço intermediário.

O motivo é o `CLAUDE.md §16`, reescrito quando a conta perdeu o Free Tier: **serviço que
cobra por existir não entra na arquitetura**. O API Gateway está ausente da tabela de Free
Tier permanente da AWS — ele cobra por requisição desde a primeira. A Function URL não tem
custo próprio: cabe na franquia da própria Lambda.

## O que foi recusado

**API Gateway HTTP API.** Traria autorizador nativo, throttling gerenciado, chave de API,
estágios e domínio customizado. **Nenhum deles é necessário aqui**, porque a aplicação já
resolve o equivalente: autenticação em middleware ASP.NET, rate limiting em
`Microsoft.AspNetCore.RateLimiting` particionado por organização, e CORS com allowlist
explícita.

Ou seja: pagar-se-ia por uma camada cujo trabalho já está feito uma camada acima.

**CloudFront na frente.** Mesma família de argumento, e acrescentaria cache que este
produto não quer — folha é dado autenticado e por tenant.

## Consequências

**Boas:** um recurso a menos, custo previsto de US$ 0,00, e nada de configuração duplicada
entre gateway e aplicação.

**Ruins:** o hostname é `*.lambda-url.<regiao>.on.aws` — feio, e **cross-site** em relação
à Vercel, o que forçou `SameSite=None` e a defesa CSRF explícita (ver ADR 004). Sem WAF
gerenciado na borda. Sem domínio customizado.

**Armadilha encontrada:** desde outubro de 2025, uma Function URL nova exige **as duas**
permissões — `lambda:InvokeFunctionUrl` **e** `lambda:InvokeFunction`. Com só a primeira, a
resposta é `403 AccessDeniedException`, que parece problema de autenticação da aplicação e
não é.
