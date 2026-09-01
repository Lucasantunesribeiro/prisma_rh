# ADR 002 — Isolamento multiempresa por filtro global

**Status:** aceita · **Data:** 2026-08 (Fase 1) · **Custo de reverter: muito alto**

## Contexto

Uma organização jamais pode ver dado de outra. É o requisito mais crítico do produto: um
erro aqui expõe folha de pagamento de empresa alheia.

## Decisão

**Filtro global no `PrismaRhDbContext`.** Toda consulta a uma entidade de tenant nasce
restrita à organização do usuário autenticado, sem ninguém escrever `where`.

Três propriedades sustentam isso:

1. **`IdOrganizacao` vem do usuário autenticado**, nunca do corpo, da query string ou de
   header. Enviar `idOrganizacao` numa requisição não tem efeito, e existe teste provando.
2. **Falha fechada:** sem usuário, o valor é `Guid.Empty` — que não casa com nada. O erro
   esconde tudo, em vez de mostrar tudo.
3. **Atravessar exige `IgnoreQueryFilters()` explícito**, que é visível em revisão de
   código e em busca por texto.

## O que foi recusado

**`where IdOrganizacao == x` escrito em cada consulta.** Funciona até a consulta número
setenta, escrita numa sexta-feira. A segurança passa a depender de ninguém esquecer — e
alguém esquece.

**Um banco por organização.** Isolamento mais forte, e custo desproporcional: migrations
multiplicadas por tenant, conexões multiplicadas, e no Neon Free isso simplesmente não
cabe. Continua sendo o caminho se um dia houver exigência contratual de isolamento físico.

**Schema por organização.** Mesma família de problemas do anterior, com menos benefício.

## Consequências

**Boas:** a proteção é estrutural. Uma consulta nova nasce segura, inclusive a que o
assistente de IA monta na Fase 11C — o modelo não consegue ampliar o próprio alcance
porque a consulta que ele propõe roda sob o mesmo filtro.

**Ruins, e é onde mora o risco real:** o filtro protege consultas dentro de uma requisição
HTTP e **mais nada**. Não alcança job assíncrono (que não tem requisição), fila, cache,
log nem arquivo. Cada um desses tem defesa própria, listada no `CLAUDE.md §24.5`, e cada
um tem teste de isolamento contra PostgreSQL real — **não** contra banco em memória, que
não gera SQL e por isso não prova nada sobre filtro global.
