# ADR 001 — Monólito modular, não microserviços

**Status:** aceita · **Data:** 2026-08 (Fase 0)

## Contexto

Folha de pagamento tem domínio grande: cadastro, contrato, vigência, rubrica, cálculo,
importação, análise, workflow, auditoria. É tentador separar cada um num serviço.

## Decisão

**Um processo, um banco, quatro projetos com dependência em uma direção só:**

```text
Api ──▶ Aplicacao ──▶ Dominio
Infraestrutura ──▶ Aplicacao / Dominio
```

O domínio não referencia ASP.NET, EF Core nem AWS SDK. Isso é verificável: os `using` do
projeto `PrismaRH.Dominio` não citam nenhum dos três.

## O que foi recusado

**Microserviços.** Eles resolvem dois problemas: times que não podem coordenar deploy, e
partes do sistema com escalas muito diferentes. **Este projeto não tem nenhum dos dois.**

O que microserviço custa e o monólito não: consistência entre serviços vira problema
distribuído. Calcular uma folha toca contrato, vigência, rubrica e parâmetro legal na
mesma transação — separá-los transformaria uma transação de banco em uma saga, com
compensação, para nenhum ganho.

**Camadas vazias "para ficar enterprise".** Não há repositório genérico sobre o EF Core,
nem Unit of Work customizado — o `DbContext` já é os dois. Não há MediatR nem AutoMapper.
Cada um deles seria uma indireção que o leitor precisa atravessar sem receber nada.

## Consequências

**Boas:** transações reais, refatoração sem contrato de rede, um deploy, um lugar para
olhar quando algo quebra.

**Ruins:** a fronteira entre módulos é convenção, não compilador — nada impede um endpoint
de importação chamar direto o motor de cálculo. É contido por revisão e pela direção de
dependência entre projetos, não por processo separado.

**Se um dia precisar mudar:** a fronteira natural é a que já existe — o worker assíncrono
da Fase 9 roda em processo próprio e conversa por fila. Foi o primeiro pedaço a sair, e
saiu porque tinha um motivo real: importação pesada não pode ocupar a requisição HTTP.
