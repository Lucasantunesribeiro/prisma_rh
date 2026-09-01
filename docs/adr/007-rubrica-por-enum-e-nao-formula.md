# ADR 007 — Rubrica com estratégia por enum, não fórmula em texto

**Status:** aceita · **Data:** 2026-08 (Fase 3) · **Custo de reverter: muito alto**

## Contexto

Cada empresa tem rubricas próprias — adicional, benefício, desconto. Um produto de folha
precisa deixar o usuário configurar isso sem alterar código.

A saída "óbvia" é um campo de fórmula: o usuário escreve `salario * 0.3` e o sistema
avalia. Praticamente todo sistema de folha comercial tem algo assim.

## Decisão

**A rubrica escolhe uma estratégia de um enum fechado**, e informa parâmetros. Não existe
campo de fórmula, nem expressão, nem script.

O mesmo desenho se repete em dois outros lugares, pelo mesmo motivo: **o motor de análises**
tem regras oficiais com parâmetros configuráveis, e não regra escrita pelo usuário; **a
consulta em linguagem natural** aceita campo, operador e valor de uma lista fechada, e não
SQL.

## O que foi recusado

**Campo de fórmula avaliado em tempo de execução.** É *template injection* / *expression
injection* com outro nome. Quem escreve a fórmula executa código no servidor — e as
bibliotecas de "expressão segura" são uma corrida armamentista com histórico ruim.

O `CLAUDE.md §24.8` fecha a classe inteira: *"parametrização nunca executa código"*.

**Fórmula compilada e revisada por um administrador.** Mistura papéis: transforma o
administrador de RH em programador, e transforma revisão de fórmula em revisão de código
sem ferramenta de revisão de código.

## Consequências

**Boas:** não há caminho do usuário para execução de código. Nem por rubrica, nem por
regra de análise, nem por pergunta em português. Isso é o que permite dizer com segurança
que a IA não executa SQL: **a fronteira não é da IA, é do produto inteiro**.

**Ruins, e são reais:** rubrica que o enum não prevê exige **alterar código e publicar**.
Um produto comercial de verdade sentiria essa limitação, e a saída correta seria ampliar o
enum com estratégias novas — nunca abrir uma porta de expressão.

**O sinal de alerta:** no dia em que alguém propuser "só um campozinho de fórmula, para o
caso raro", esta ADR é a resposta.
