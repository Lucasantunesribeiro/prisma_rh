# ADR 005 — PostgreSQL no Neon, e não RDS

**Status:** aceita · **Data:** 2026-08 (Fase 0), revista na Fase 10

## Contexto

O produto é relacional por natureza: contrato tem vigências, folha tem holerites,
holerite tem lançamentos, lançamento tem memória de cálculo. Precisa de transação,
constraint e histórico.

## Decisão

**PostgreSQL**, em Docker Compose no desenvolvimento e **Neon** na produção.

O PostgreSQL não foi escolhido por ser popular. Foi escolhido por três recursos que o
domínio usa de verdade:

1. **Exclusion constraint** — `ex_vigencias_sem_sobreposicao` impede duas vigências
   contratuais sobrepostas **mesmo sob requisições simultâneas**. Em C# isso seria uma
   verificação com corrida embutida: dois pedidos leem "não há sobreposição" e ambos
   gravam.
2. **`numeric` exato** — dinheiro nunca em ponto flutuante.
3. **`pg_advisory_xact_lock`** — usado no orçamento global de blobs. Escopo de
   **transação**, não de sessão, porque atrás do PgBouncer em modo transaction a sessão
   não é sua.

## O que foi recusado

**AWS RDS.** Cobra **por hora provisionada**, exista ou não tráfego. Numa conta sem Free
Tier — o caso deste portfólio — isso é cobrança por existir, e a decisão registrada no
`CLAUDE.md §16` exclui essa classe inteira.

**DynamoDB.** Escala e custa por uso, mas o domínio é relacional e transacional. Modelar
"a folha de agosto com seus holerites, lançamentos e memória" em chave-valor seria
inventar joins na aplicação.

**SQLite.** Suficiente para desenvolvimento, insuficiente para os três recursos acima e
para concorrência real.

## Consequências

**Boas:** custo zero na produção do portfólio, transações reais, constraints que valem
mesmo quando o C# erra.

**Ruins:** o Neon Free suspende o banco quando ocioso — a primeira requisição depois de um
tempo parado paga o *cold start* do banco **além** do da Lambda.

**Cuidado que isso impôs:** o pooler do Neon fica na frente. `MaxPoolSize` é pequeno de
propósito, e nenhum lock de **sessão** pode ser usado — daí `pg_advisory_xact_lock` em vez
de `pg_advisory_lock`. Isso não aparece em teste local sem pooler: é o tipo de defeito que
só existe em produção.
