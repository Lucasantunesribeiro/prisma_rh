# ROADMAP.md — Prisma RH

> **Plano oficial de evolução do Prisma RH**
>
> Este documento define todas as fases do projeto, sua ordem obrigatória, objetivos, entregas, limites, critérios de aceite e condições para avançar.
>
> Nenhum agente pode implementar funcionalidades de uma fase futura sem autorização explícita do responsável pelo projeto.

---

# 0. COMO USAR ESTE DOCUMENTO

Antes de qualquer tarefa:

1. Ler integralmente o `CLAUDE.md`.
2. Ler integralmente este `ROADMAP.md`.
3. Identificar a fase atual.
4. Executar apenas tarefas pertencentes à fase atual.
5. Não preparar antecipadamente estruturas de fases futuras.
6. Não instalar tecnologias que só serão necessárias posteriormente.
7. Não criar abstrações "para o futuro" sem uso real na fase atual.
8. Não avançar de fase automaticamente.
9. Ao concluir uma fase, parar.
10. A próxima fase só começa quando o responsável pelo projeto autorizar explicitamente.

## Regra principal

Conhecer o destino do projeto **não autoriza antecipá-lo**.

Exemplo:

Saber que futuramente haverá SQS não autoriza criar fila, abstração de mensageria, interfaces de broker ou configuração AWS durante a Fase 0.

---

# 1. VISÃO FINAL DO PRISMA RH

O Prisma RH será uma plataforma B2B brasileira para:

- gestão multiempresa;
- cadastro de funcionários;
- gestão contratual;
- cálculo de folha;
- armazenamento histórico das folhas;
- memória de cálculo;
- importação de dados;
- validação automática;
- análise de inconsistências;
- auditoria;
- workflows de tratamento;
- integrações externas;
- processamento assíncrono;
- operação em produção.

O sistema deve poder funcionar sozinho.

Integrações com sistemas externos serão complementares e não serão necessárias para o funcionamento central do produto.

---

# 2. VISÃO DE ARQUITETURA FINAL

A arquitetura final planejada é:

```text
                    ┌─────────────────────┐
                    │       Vercel        │
                    │ React + TypeScript  │
                    └──────────┬──────────┘
                               │
                               ▼
                    ┌─────────────────────┐
                    │ API Gateway HTTP API│
                    └──────────┬──────────┘
                               │
                               ▼
                    ┌─────────────────────┐
                    │    Lambda .NET      │
                    │       API           │
                    └───────┬─────┬───────┘
                            │     │
                            │     ▼
                            │   AWS S3
                            │     │
                            │     ▼
                            │   AWS SQS
                            │     │
                            │     ▼
                            │ Lambda Worker
                            │     │
                            ▼     ▼
                      Neon PostgreSQL
```

Essa arquitetura é o destino final.

Ela não deve existir desde o início.

O projeto deve começar simples e ganhar complexidade apenas quando houver uma necessidade concreta.

---

# 3. STACK FINAL PREVISTA

## Backend

- C#
- .NET 10
- ASP.NET Core Web API
- Entity Framework Core
- PostgreSQL

## Frontend

- React
- TypeScript
- Vite
- Tailwind CSS
- shadcn/ui

## Produção

- Frontend: Vercel
- API: AWS Lambda
- Gateway: API Gateway HTTP API
- Arquivos: AWS S3
- Processamento assíncrono: AWS SQS + Lambda Worker
- Banco: Neon PostgreSQL
- Logs: AWS CloudWatch
- CI/CD: GitHub Actions

## Desenvolvimento local

- Docker Compose
- PostgreSQL
- backend local
- frontend local

---

# 4. REGRAS DE PROGRESSÃO

Cada fase possui:

- objetivo;
- escopo;
- entregas;
- fora de escopo;
- critérios de aceite;
- condição para avançar.

Uma fase só é considerada concluída quando seus critérios de aceite forem cumpridos.

Não é obrigatório concluir todas as funcionalidades imagináveis de uma fase.

É obrigatório concluir corretamente o que foi definido para ela.

---

# FASE 0 — FUNDAÇÃO DO PROJETO

## Objetivo

Criar uma base técnica simples, reproduzível e profissional.

Não existe domínio de RH nesta fase.

## Entregas

### Repositório

Estrutura esperada:

```text
prisma-rh/
├── backend/
│   ├── src/
│   │   ├── PrismaRH.Api/
│   │   ├── PrismaRH.Aplicacao/
│   │   ├── PrismaRH.Dominio/
│   │   └── PrismaRH.Infraestrutura/
│   ├── testes/
│   └── PrismaRH.sln
├── frontend/
├── docker-compose.yml
├── .env.example
├── .gitignore
├── CLAUDE.md
├── ROADMAP.md
└── README.md
```

### Backend

- .NET 10;
- ASP.NET Core Web API;
- projetos base;
- referência correta entre projetos;
- Entity Framework Core;
- provider PostgreSQL;
- configuração por ambiente;
- OpenAPI;
- health check;
- tratamento técnico básico de erro;
- aplicação compilando.

### Frontend

- React;
- TypeScript;
- Vite;
- Tailwind;
- shadcn/ui preparado;
- tela inicial simples;
- health check da API;
- estados de carregamento, sucesso e erro.

### Infra local

- PostgreSQL via Docker Compose;
- volume local;
- health check do banco;
- configuração por `.env`.

### Qualidade

- xUnit configurado;
- Vitest configurado;
- build backend;
- build frontend;
- testes mínimos da fundação.

### Documentação

README com:

- objetivo do projeto;
- stack atual;
- pré-requisitos;
- setup local;
- comandos de execução;
- comandos de teste.

## Fora de escopo

Não implementar:

- usuários;
- autenticação;
- organização;
- empresa;
- funcionários;
- folha;
- rubricas;
- cálculo;
- importação;
- AWS;
- Redis;
- filas;
- CI/CD de produção.

## Critérios de aceite

- PostgreSQL sobe com Docker Compose;
- backend inicia;
- frontend inicia;
- frontend consulta `/health`;
- backend compila;
- testes backend passam;
- frontend compila;
- testes frontend passam;
- nenhuma funcionalidade de domínio foi criada.

## Liberação da próxima fase

Somente após revisão e aprovação explícita.

---

# FASE 1 — IDENTIDADE, AUTENTICAÇÃO E MULTIEMPRESA

## Objetivo

Criar a estrutura de acesso e isolamento entre organizações.

Essa fase estabelece a base de segurança do sistema.

## Entidades iniciais

### Organização

Tenant principal.

Pode representar:

- BPO de RH;
- escritório de contabilidade;
- grupo empresarial;
- departamento pessoal centralizado.

### Usuário

Pessoa com acesso ao Prisma RH.

### Empresa

Empresa administrada por uma organização.

### Estabelecimento

Filial/unidade vinculada a uma empresa.

## Perfis

- Administrador da Plataforma;
- Administrador da Empresa;
- Analista de RH;
- Auditor;
- Visualizador.

## Entregas

### Autenticação

- login;
- senha com hash seguro;
- JWT access token;
- refresh token;
- logout;
- rotação/revogação de refresh token;
- usuário atual.

### Autorização

- autorização no backend;
- políticas por perfil;
- rotas protegidas;
- frontend adaptado ao perfil.

### Multiempresa

- organização;
- empresas;
- estabelecimentos;
- usuários associados à organização;
- validação de isolamento.

### Segurança

- nenhum tenant acessa outro;
- nenhum identificador enviado pelo frontend pode furar isolamento;
- testes de autorização;
- testes de isolamento.

### Frontend

- login;
- sessão;
- seletor/contexto de empresa quando aplicável;
- administração básica de empresas e estabelecimentos.

## Fora de escopo

- funcionários;
- contratos;
- folha;
- cálculos;
- importação;
- AWS.

## Critérios de aceite

- login funciona;
- refresh funciona;
- logout revoga sessão conforme estratégia aprovada;
- perfil limita operações;
- Organização A não acessa Organização B;
- empresas e estabelecimentos respeitam tenant;
- testes de isolamento passam.

---

# FASE 2 — CADASTRO FUNCIONAL DE RH

## Objetivo

Criar os dados necessários para representar a relação de trabalho.

## Entidades principais

- Funcionário;
- ContratoTrabalho;
- VigenciaContrato;
- Cargo;
- SituaçãoContratual.

> **Decisão registrada em 23/08/2026 — histórico por vigência única.**
>
> `HistóricoSalarial` constava aqui como entidade separada, ao lado de Lotação e
> Jornada. Foi substituída por **`VigenciaContrato`**: uma única linha por período,
> guardando salário, cargo, estabelecimento e jornada juntos, com `valido_de` e
> `valido_ate`.
>
> Motivo: a pergunta que o motor de cálculo da Fase 3 fará para cada contrato, em
> cada competência, é sempre *"como este contrato estava nesta data?"*. Com tudo
> num registro só, isso é uma consulta. Com históricos separados por tipo, seriam
> três ou mais junções por faixa de data — e cada nova dimensão futura viraria mais
> uma tabela e mais uma junção.
>
> Lotação e Jornada deixam de ser entidades e passam a ser campos da vigência.
> Dependente sai da Fase 2 e entra na **Fase 4D**, junto com o IRRF, que é a regra
> que realmente usa dependentes.

## Dados mínimos

Definir somente campos necessários para os cálculos planejados.

Exemplos:

- matrícula;
- nome;
- documento fictício em demo;
- data de admissão;
- situação;
- salário;
- estabelecimento;
- cargo;
- jornada;
- histórico salarial;
- datas relevantes.

## Histórico

Alterações importantes não podem destruir o passado.

Exemplos:

- alteração salarial;
- mudança de estabelecimento;
- mudança de cargo;
- mudança contratual.

## Frontend

- listagem;
- cadastro;
- edição;
- consulta;
- histórico quando aplicável;
- filtros;
- estados vazios/erro/carregamento.

## Fora de escopo

- cálculo da folha;
- importação em massa;
- regras legais complexas;
- AWS.

## Critérios de aceite

- funcionário pertence à organização correta;
- vínculo com empresa/estabelecimento funciona;
- histórico salarial preserva vigência;
- alterações não reescrevem passado indevidamente;
- telas operacionais funcionam;
- testes de domínio principais passam.

---

# FASE 3 — NÚCLEO DA FOLHA MENSAL

## Objetivo

Criar a primeira folha mensal calculada pelo próprio Prisma RH.

Essa é a primeira fase em que o sistema efetivamente calcula e armazena folha.

## Conceitos principais

### Competência

Representa mês/ano do processamento.

Exemplo:

```text
08/2026
```

Não tratar competência apenas como string solta.

### FolhaPagamento

Representa um processamento de folha.

Estados iniciais previstos:

```text
Rascunho
EmCalculo
Calculada
ComInconsistencias
Fechada
```

Estados finais exatos poderão ser ajustados na especificação da fase.

### Rubrica

Evento de folha.

Exemplos futuros:

- salário;
- hora extra;
- adicional;
- desconto;
- benefício.

### LançamentoFolha

Valor que participa da folha de determinado funcionário.

### MemoriaCalculo

Explica como cada valor foi obtido.

## Primeira versão do cálculo

Começar com poucas rubricas.

O conjunto exato deverá ser definido antes da implementação.

> **Escopo aprovado pelo responsável em 23/08/2026 — primeira folha mensal.**
>
> **Rubricas.** Salário-base proporcional (calculado pelo sistema) mais lançamentos
> manuais de provento e desconto, com totais e líquido. Nenhum encargo legal: INSS,
> FGTS, IRRF, hora extra e DSR dependem de fonte oficial e de parâmetro versionado, e
> pertencem à Fase 4.
>
> **Elegibilidade.** Entra na folha quem teve vínculo em **qualquer dia** da
> competência. Admitido dia 20 entra proporcional; desligado dia 10 também, porque
> trabalhou esses dias. O critério alternativo — "ativo no último dia" — deixaria
> trabalho feito sem pagamento até a Fase 4G.
>
> **Divisor.** O mês vale **30 avos**, tenha 28, 30 ou 31 dias (CLT art. 64, que fixa o
> divisor 30 para o salário mensal). Quem trabalha fevereiro inteiro recebe salário
> cheio; o dia 31 não gera um trigésimo primeiro avo.
>
> **Vigência dentro da competência.** Um aumento no meio do mês é **repartido**: cada
> vigência recebe os avos que lhe cabem. Pagar o mês inteiro pelo salário novo
> reescreveria, na prática, a primeira metade do mês.
>
> **Arredondamento** (`CLAUDE.md §28`). Duas casas, `MidpointRounding.AwayFromZero`,
> aplicado no valor final de **cada rubrica**; os totais somam parcelas já arredondadas.
> O padrão `ToEven` do .NET faria o funcionário perder um centavo pela paridade do
> dígito anterior, sem forma de entender o motivo.

> **Decisão registrada em 23/08/2026 — estados da folha.**
>
> Dos cinco estados previstos, ficaram três: **Rascunho, Calculada, Fechada**.
>
> `EmCalculo` foi descartado porque o cálculo desta fase é síncrono — o estado só
> existiria entre duas linhas de código e nunca seria observável. Volta a fazer sentido
> na Fase 9, com processamento assíncrono.
>
> `ComInconsistencias` foi descartado porque não há motor de análises antes da Fase 6.
> Criar o estado agora seria montar estrutura de fase futura.
>
> **Não existe reabertura de folha fechada nesta fase**, e isso é deliberado: o próprio
> ROADMAP manda exigir "fluxo explícito futuro" depois do fechamento, e um método de
> reabrir sem esse fluxo seria a sobrescrita silenciosa que o documento proíbe.

> **Decisão registrada em 23/08/2026 — memória de cálculo como passos.**
>
> `MemoriaCalculo` virou uma **coleção** de `LinhaMemoriaCalculo` por lançamento, em vez
> de um campo de texto único. Um valor de folha quase nunca sai de uma conta só —
> salário da vigência, avos do mês, multiplicação — e guardar apenas o resultado
> impediria o que o `CLAUDE.md §4.2` exige: descobrir quais bases levaram àquele número.
>
> Cada lançamento congela o **código, o nome e o tipo** da rubrica no momento do
> cálculo. Renomear a rubrica no ano seguinte não pode reescrever um holerite fechado.

Exemplo de primeira evolução possível:

1. salário-base;
2. salário proporcional;
3. lançamento manual simples;
4. total de proventos;
5. total de descontos;
6. líquido.

Não adicionar encargos legais sem especificação própria.

## Reprocessamento

Antes do fechamento:

- recalcular;
- substituir resultado anterior de maneira controlada;
- manter rastreabilidade adequada.

Depois do fechamento:

- não sobrescrever silenciosamente;
- exigir fluxo explícito futuro.

## Critérios de aceite

- criar competência;
- abrir folha;
- incluir funcionários elegíveis;
- calcular primeira versão;
- persistir resultados;
- exibir memória de cálculo;
- reprocessar de maneira previsível;
- testes cobrem regras e arredondamentos.

---

# FASE 4 — REGRAS BRASILEIRAS DE FOLHA

## Objetivo

Evoluir o cálculo para se aproximar de um sistema brasileiro real.

Essa fase deve ser dividida em subfases.

Nunca implementar tudo de uma vez.

---

## FASE 4A — INCIDÊNCIAS E BASES

### Objetivo

Criar estrutura adequada para bases e incidências.

### Entregas

- incidências por rubrica;
- bases intermediárias;
- memória de cálculo;
- testes.

---

## FASE 4B — INSS

### Objetivo

Implementar INSS de acordo com regras oficiais vigentes.

### Requisitos

- tabelas por vigência;
- faixas parametrizadas;
- teto;
- memória de cálculo;
- testes de faixa;
- testes de limite;
- testes de mudança de vigência.

---

## FASE 4C — FGTS

### Objetivo

Calcular base e valor de FGTS quando aplicável.

### Requisitos

- incidências;
- parâmetros versionados;
- memória;
- testes.

---

## FASE 4D — IRRF

### Objetivo

Implementar IRRF quando o domínio já possuir os dados necessários.

### Requisitos

- tabelas por vigência;
- dependentes quando aplicável;
- deduções;
- memória;
- testes.

---

## FASE 4E — FÉRIAS

### Objetivo

Suportar processamento de férias.

### Entregas futuras

- período aquisitivo;
- período concessivo;
- gozo;
- remuneração;
- 1/3;
- incidências;
- memória;
- testes.

---

## FASE 4F — 13º SALÁRIO

### Objetivo

Suportar processamento de 13º.

### Entregas

- avos;
- primeira/segunda parcela quando aplicável;
- bases;
- incidências;
- memória;
- testes.

---

## FASE 4G — RESCISÃO

### Objetivo

Suportar cálculo de desligamento.

### Entregas

- data;
- motivo;
- verbas aplicáveis;
- saldo;
- férias;
- 13º;
- descontos;
- memória;
- testes.

---

## Regra para toda Fase 4

Antes de implementar qualquer regra legal:

1. localizar fonte oficial;
2. documentar fonte;
3. documentar vigência;
4. modelar parâmetros;
5. implementar;
6. testar limites;
7. revisar resultado.

---

# FASE 5 — IMPORTAÇÃO CSV/XLSX

## Objetivo

Permitir entrada de dados em massa.

## Casos de uso

Inicialmente:

- funcionários;
- contratos;
- alterações;
- lançamentos da folha.

Posteriormente:

- dados de sistemas externos;
- folhas importadas para conferência/migração.

## Fluxo

```text
Upload
  ↓
Validação do arquivo
  ↓
Leitura
  ↓
Preview
  ↓
Validação de registros
  ↓
Confirmação
  ↓
Persistência
  ↓
Relatório
```

## Requisitos

- CSV;
- XLSX;
- limites de tamanho;
- validação de estrutura;
- relatório linha a linha;
- erros compreensíveis;
- transação quando apropriada;
- idempotência quando necessário;
- origem registrada.

## Segurança

- não executar macros;
- não confiar na extensão;
- evitar fórmulas perigosas ao exportar;
- limites de quantidade;
- proteção contra arquivos malformados.

## Fora de escopo

S3/SQS inicialmente.

Primeiro implementar local/síncrono para compreender o fluxo.

## Critérios de aceite

- arquivo válido importa;
- arquivo inválido não corrompe dados;
- usuário vê erros;
- preview funciona;
- origem fica rastreável.

---

# FASE 6 — MOTOR DE ANÁLISES

## Objetivo

Criar a principal camada de conferência da folha.

Depois de calculada/importada, a folha será analisada por regras.

## Estratégia

Regras fornecidas pelo Prisma RH com parâmetros configuráveis.

Usuário não escreve código.

## Estrutura prevista

```text
RegraAnalise
VersaoRegra
ParametroRegra
ExecucaoAnalise
ResultadoAnalise
```

## Primeiras categorias possíveis

- cadastro;
- contrato;
- salário;
- lançamentos;
- benefícios;
- encargos;
- desligamentos;
- duplicidades;
- ausência;
- divergências de base;
- valores fora de tolerância.

## Exemplos

- funcionário desligado com lançamento incompatível;
- salário abaixo/acima do esperado;
- rubrica duplicada;
- funcionário elegível ausente;
- base de FGTS incompatível;
- diferença superior à tolerância.

## Cada resultado deve registrar

- regra;
- versão;
- folha;
- funcionário;
- descrição;
- valor esperado;
- valor encontrado;
- diferença;
- severidade;
- data;
- contexto técnico suficiente.

## Critérios de aceite

- regras versionadas;
- execução reproduzível;
- resultados persistidos;
- parâmetros configuráveis;
- testes;
- nenhum código arbitrário do usuário.

---

# FASE 7 — WORKFLOW DE TRATAMENTO E AUDITORIA

## Objetivo

Transformar o Prisma RH de um calculador/validador em uma ferramenta operacional.

## Status

```text
Detectada
   ↓
Em análise
   ↓
Justificada
   ↓
Corrigida
   ↓
Resolvida
```

## Funcionalidades

- atribuir responsável;
- comentários;
- justificativas;
- evidências;
- histórico;
- filtros;
- SLA futuro se aprovado;
- reabertura se necessária;
- auditoria.

## Auditoria

Registrar eventos importantes como:

- alteração de funcionário;
- alteração salarial;
- cálculo;
- fechamento;
- reprocessamento;
- justificativa;
- mudança de status;
- alteração de regra;
- alteração de parâmetro.

## Dashboard

Indicadores como:

- folhas processadas;
- inconsistências;
- severidade;
- percentual de conformidade;
- pendências por responsável;
- regras com maior incidência;
- evolução por competência.

## Critérios de aceite

- histórico não é apagado;
- usuário consegue entender quem fez o quê;
- status respeita transições;
- auditor possui visão suficiente;
- dashboard usa dados reais do sistema.

---

# FASE 8 — INTEGRAÇÕES EXTERNAS VIA API

## Objetivo

Permitir comunicação com sistemas externos.

O Prisma RH continua funcionando sem qualquer integração.

## Arquitetura

Criar adaptadores por fornecedor.

Exemplo:

```text
Aplicacao
   ↓
IIntegracaoFolha
   ↓
├── IntegracaoFornecedorA
├── IntegracaoFornecedorB
└── IntegracaoGenerica
```

A interface e nomes finais só deverão existir quando houver integração real a implementar.

Não criar abstrações vazias antecipadamente.

## Possíveis integrações

- ERP;
- HRIS;
- ponto;
- benefícios;
- folha externa;
- sistemas internos de empresas.

## Fluxos possíveis

### Entrada

```text
Sistema externo
      ↓
    API
      ↓
 Prisma RH
```

### Saída

```text
Prisma RH
   ↓
Sistema externo
```

## Requisitos

- autenticação segura;
- timeouts;
- retry somente quando seguro;
- idempotência;
- logs;
- correlation id;
- limites;
- tratamento de erro;
- mapeamento explícito.

## Custos

Antes de qualquer integração:

- confirmar se API é gratuita;
- verificar documentação;
- verificar limites;
- verificar licença;
- verificar custo externo.

O teto AWS continua US$ 6,50/mês.

---

# FASE 9 — PROCESSAMENTO ASSÍNCRONO NA AWS

## Objetivo

Mover trabalhos pesados para processamento assíncrono quando o produto já tiver necessidade concreta.

## Casos esperados

- grandes importações;
- cálculo em lote;
- execução de muitas regras;
- geração de relatórios;
- integrações longas.

## Arquitetura principal

```text
Usuário
  ↓
Frontend
  ↓
API
  ↓
S3
  ↓
SQS
  ↓
Lambda Worker
  ↓
PostgreSQL
```

## Recursos AWS previstos

- S3;
- SQS;
- Lambda;
- CloudWatch.

## Conceitos obrigatórios

- idempotência;
- retry;
- DLQ quando necessária;
- correlation id;
- status do processamento;
- falha parcial;
- timeout;
- tamanho de lote;
- logs;
- métricas básicas.

## O que não usar por padrão

- EC2;
- RDS;
- ECS/Fargate;
- NAT Gateway;
- Kafka;
- RabbitMQ;
- EKS.

## Critérios de aceite

- API não fica bloqueada em trabalho pesado;
- processamento pode falhar e ser rastreado;
- retry não duplica efeitos;
- usuário consulta status;
- custo permanece dentro do orçamento.

---

# FASE 10 — DEPLOY E PRODUÇÃO

## Objetivo

Tornar o Prisma RH 100% demonstrável publicamente.

## Frontend

- Vercel;
- HTTPS;
- variáveis de ambiente;
- build automatizado.

## Backend

- AWS Lambda;
- API Gateway HTTP API.

## Banco

- Neon PostgreSQL.

## Arquivos/processamento

Se já implementados:

- S3;
- SQS;
- Lambda Worker.

## Observabilidade

- CloudWatch;
- logs estruturados;
- correlation id;
- health;
- erros rastreáveis.

## CI/CD

GitHub Actions:

- backend build;
- backend tests;
- frontend lint;
- frontend tests;
- frontend build;
- deploy somente conforme estratégia aprovada.

## Demo

Criar:

- organização fictícia;
- empresas fictícias;
- funcionários fictícios;
- folhas fictícias;
- inconsistências demonstráveis;
- contas demo por perfil quando seguro.

## Critérios de aceite

Um recrutador deve conseguir:

1. abrir o site;
2. entrar;
3. entender o produto;
4. navegar por empresa;
5. consultar funcionários;
6. abrir/processar uma folha;
7. visualizar cálculos;
8. visualizar inconsistências;
9. tratar uma inconsistência;
10. consultar histórico.

---

# FASE 11 — HARDENING E QUALIDADE DE PRODUÇÃO

## Objetivo

Revisar o produto como se estivesse sendo preparado para uso empresarial.

## Segurança

- revisão de autorização;
- revisão multiempresa;
- rate limiting;
- CORS;
- cookies/tokens;
- uploads;
- secrets;
- dependências;
- headers;
- logs;
- dados sensíveis.

## Performance

- queries;
- índices;
- paginação;
- N+1;
- payloads;
- lotes;
- cold start;
- uso de memória.

## Testes

- cenários críticos;
- integração;
- E2E;
- isolamento;
- regressão de cálculo;
- falhas assíncronas.

## Banco

- migrations;
- constraints;
- índices;
- concorrência;
- backups do provedor;
- estratégia de recuperação documentada.

## Critérios de aceite

- nenhum problema crítico conhecido;
- fluxos principais cobertos;
- auditoria funcionando;
- documentação atualizada;
- custos controlados.

---

# FASE 12 — DOCUMENTAÇÃO DE PORTFÓLIO E ENTREVISTA

## Objetivo

Transformar o projeto construído em evidência clara de conhecimento técnico.

## README final

Deve conter:

- problema;
- produto;
- arquitetura;
- stack;
- decisões técnicas;
- trade-offs;
- screenshots;
- demo;
- setup;
- testes;
- segurança;
- custos;
- limitações.

## Documentação técnica

Criar somente documentação útil:

- arquitetura;
- modelo de multi-tenancy;
- cálculo;
- motor de regras;
- processamento assíncrono;
- segurança;
- custos AWS;
- ADRs para decisões realmente relevantes.

## Material para entrevistas

O autor deve conseguir explicar:

### Backend

- ASP.NET Core;
- DI;
- middleware;
- EF Core;
- transações;
- PostgreSQL;
- concorrência;
- autenticação;
- autorização;
- multi-tenancy.

### Domínio

- competência;
- rubrica;
- cálculo;
- memória;
- vigência;
- histórico;
- inconsistências.

### AWS

- Lambda;
- API Gateway;
- S3;
- SQS;
- retry;
- DLQ;
- idempotência;
- custos.

### Arquitetura

- por que monólito modular;
- por que não microserviços;
- por que PostgreSQL;
- por que serverless;
- por que não RDS;
- por que processamento assíncrono surgiu apenas depois.

---

# 5. MAPA DE EVOLUÇÃO RESUMIDO

```text
FASE 0
Fundação técnica
    ↓
FASE 1
Identidade + Multiempresa
    ↓
FASE 2
Cadastro RH
    ↓
FASE 3
Folha mensal inicial
    ↓
FASE 4
Regras brasileiras
    ↓
FASE 5
Importação CSV/XLSX
    ↓
FASE 6
Motor de análises
    ↓
FASE 7
Workflow + Auditoria
    ↓
FASE 8
Integrações externas
    ↓
FASE 9
AWS assíncrona
    ↓
FASE 10
Produção
    ↓
FASE 11
Hardening
    ↓
FASE 12
Portfólio + domínio técnico
```

---

# 6. TECNOLOGIAS E QUANDO ELAS ENTRAM

| Tecnologia | Fase mínima | Motivo |
|---|---:|---|
| .NET 10 | 0 | Backend principal |
| React | 0 | Frontend |
| PostgreSQL | 0 | Persistência principal |
| Docker Compose | 0 | Ambiente local |
| JWT | 1 | Autenticação |
| EF Core | 0 | Persistência |
| CSV/XLSX | 5 | Importação |
| AWS S3 | 9 | Arquivos/entrada assíncrona |
| AWS SQS | 9 | Processamento assíncrono |
| AWS Lambda Worker | 9 | Trabalho em background |
| API Gateway | 10 | API pública |
| Lambda API | 10 | Backend serverless |
| CloudWatch | 9/10 | Operação |
| GitHub Actions | 10 | CI/CD de produção |

## Tecnologias sem fase definida

Estas não entram automaticamente:

- Redis;
- RabbitMQ;
- Kafka;
- Kubernetes;
- CQRS;
- MediatR;
- Event Sourcing;
- GraphQL;
- Elasticsearch;
- IA.

Só entram mediante nova decisão arquitetural aprovada.

---

# 7. CUSTO AWS

## Limite

Máximo:

**US$ 6,50 por mês para o Prisma RH.**

## Meta

Preferencialmente:

**US$ 0 a US$ 1/mês em uso normal de portfólio.**

## Regra

Nenhuma fase autoriza automaticamente criar recurso pago.

Antes de qualquer recurso AWS:

1. listar recurso;
2. justificar;
3. estimar custo;
4. verificar alternativa gratuita;
5. pedir autorização;
6. criar somente depois da aprovação.

---

# 8. O QUE NÃO DEVE SER FEITO

O agente não deve:

- construir várias fases numa tarefa;
- preparar código "para quando chegar a AWS";
- criar abstrações genéricas de broker;
- instalar Redis porque "pode ser útil";
- usar microserviços porque o domínio é grande;
- implementar regra brasileira sem fonte;
- hardcodar tabela legal;
- criar dashboard com dados falsos antes do domínio;
- criar telas sem backend real apenas para aparência;
- criar funcionalidades não solicitadas;
- alterar roadmap sozinho.

---

# 9. COMO UMA FASE DEVE SER EXECUTADA

Para cada fase:

## 1. Planejar

Antes de modificar:

- ler código atual;
- identificar lacunas;
- decompor em tarefas pequenas;
- indicar riscos.

## 2. Implementar incrementalmente

Cada tarefa deve:

- ter escopo pequeno;
- possuir resultado verificável;
- não quebrar fase anterior.

## 3. Testar

- build;
- testes;
- lint;
- integração quando aplicável.

## 4. Revisar

- segurança;
- multi-tenancy;
- regra de negócio;
- migrations;
- escopo.

## 5. Parar

Ao concluir a fase:

- apresentar resumo;
- apresentar pendências;
- não iniciar próxima fase.

---

# 10. DEFINITION OF READY PARA A PRÓXIMA FASE

Uma fase seguinte só pode começar quando:

- fase atual foi implementada;
- testes relevantes passam;
- build passa;
- erros conhecidos foram avaliados;
- documentação atual está coerente;
- responsável revisou o resultado;
- responsável autorizou explicitamente avançar.

Sem autorização:

```text
PARE.
```

---

# 11. PRINCÍPIO FINAL

O objetivo não é terminar o Prisma RH o mais rápido possível.

O objetivo é construir um sistema em que:

- cada etapa tenha motivo;
- cada regra seja entendida;
- cada tecnologia seja justificável;
- cada decisão possa ser explicada;
- o autor domine o projeto por completo.

Velocidade nunca deve substituir entendimento.

---

**Fim do ROADMAP.md**
