# Decisões de arquitetura (ADR)

Um ADR registra **uma decisão que custa caro reverter**, com o que se sabia na hora e o que
foi recusado. O valor não está na decisão — está na **alternativa descartada e no motivo**.

Por isso todo ADR aqui tem uma seção *"O que foi recusado"*. Sem ela, o documento vira um
elogio à própria escolha.

## O que entra aqui, e o que não entra

Entra decisão **estrutural**: a que outras decisões passam a depender, e que muda o custo
de tudo que vier depois.

Não entra escolha de implementação, nome de classe nem preferência de estilo. O `ROADMAP.md`
já registra as decisões de cada fase; um ADR para cada uma delas transformaria a pasta num
segundo roadmap que ninguém lê.

## Índice

| # | Decisão | Custo de reverter |
|---|---|---|
| [001](001-monolito-modular.md) | Monólito modular, não microserviços | Alto |
| [002](002-isolamento-por-filtro-global.md) | Isolamento multiempresa por filtro global | **Muito alto** — toda consulta depende |
| [003](003-404-em-vez-de-403.md) | Recurso de outro tenant devolve 404, não 403 | Baixo no código, alto na confiança |
| [004](004-token-em-memoria-refresh-em-cookie.md) | Access token em memória, refresh opaco em cookie | Alto — muda backend e frontend |
| [005](005-postgresql-e-nao-rds.md) | PostgreSQL no Neon, e não RDS | Médio |
| [006](006-function-url-em-vez-de-api-gateway.md) | Lambda Function URL, não API Gateway | Baixo |
| [007](007-rubrica-por-enum-e-nao-formula.md) | Rubrica com estratégia por enum, não fórmula em texto | **Muito alto** — é a fronteira que impede execução de código do usuário |
| [008](008-ia-explica-e-nao-calcula.md) | A IA explica; ela não calcula | Alto |
