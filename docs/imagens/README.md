# Imagens do portfólio

Esta pasta guarda as capturas de tela referenciadas no `README.md`.

> ⚠️ **As capturas ainda não foram feitas, e o motivo está registrado.** Tirá-las exige
> **entrar na aplicação**, e digitar senha em formulário é ação que o agente que construiu
> este projeto não executa. A lista abaixo existe para que a captura leve cinco minutos.

## O que capturar

Ordem escolhida para contar a história do produto em seis imagens — de "o que é" até "por
que é confiável".

| # | Arquivo | Tela | Por que esta |
|---|---|---|---|
| 1 | `01-painel.png` | `/painel` | Primeira impressão: indicadores, pendências por responsável, evolução por competência |
| 2 | `02-folha-detalhe.png` | `/folhas/{id}`, com um holerite aberto | O centro do produto: totais, bases de INSS/FGTS/IRRF e lançamentos |
| 3 | `03-memoria-calculo.png` | O mesmo holerite, memória expandida | **A imagem mais importante.** É o diferencial: o número **e a conta que levou até ele** |
| 4 | `04-inconsistencias.png` | `/inconsistencias`, com a gaveta aberta | Workflow, linha do tempo e o assistente de IA rotulado |
| 5 | `05-pergunta-em-portugues.png` | `/inconsistencias`, após uma consulta | Mostrando **"Entendi como: Severidade = Alta e Status ≠ Resolvida"** — a prova de que o modelo propõe e a aplicação decide |
| 6 | `06-auditoria.png` | `/auditoria` | Trilha somente-inserção: quem, o quê, quando, de quanto para quanto |

## Regras para as capturas

Estas não são preferências estéticas — são o Security Gate da Fase 13, item 2.

- **Só o ambiente de demonstração**, com a base fictícia. Nenhum CPF, salário ou nome real.
- **Nenhuma URL interna, nome de bucket, ARN, id de conta AWS ou string de conexão** —
  nem no conteúdo, nem na barra de endereço, nem numa aba vizinha.
- **Nenhum terminal com credencial**, nem truncada.
- **Nenhuma aba do navegador** com outro sistema aberto.
- Janela em ~1440×900. Mais largo espreme a densidade; mais estreito ativa o layout
  compacto e a tela parece outra coisa.
- Tema claro, para legibilidade em README no GitHub.

> Documentar **arquitetura** de segurança em portfólio é positivo e esperado. Publicar a
> **configuração exata** que a implementa, não.

## Depois de capturar

Referencie no `README.md` logo abaixo da seção *"O que ele faz"*, no formato:

```markdown
| ![Memória de cálculo](docs/imagens/03-memoria-calculo.png) |
|:--:|
| *Cada valor guarda os passos que o produziram — e qual parâmetro estava vigente.* |
```

A legenda importa tanto quanto a imagem: quem passa os olhos lê a legenda e não a tela.
