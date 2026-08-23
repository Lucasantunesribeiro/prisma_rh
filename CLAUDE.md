# CLAUDE.md — Prisma RH

> **Fonte de verdade operacional do projeto.**
>
> Este arquivo define o produto, o domínio, o escopo, a arquitetura, a stack, as regras de negócio, o roadmap e as regras obrigatórias para qualquer agente de IA que trabalhe neste repositório.

---

## 0. REGRA OBRIGATÓRIA PARA QUALQUER TAREFA

Antes de iniciar **qualquer** tarefa neste repositório:

1. Leia este arquivo integralmente.
2. Identifique em qual fase do roadmap a tarefa se encontra.
3. Confirme internamente que a implementação respeita o escopo da fase atual.
4. Inspecione o código existente antes de propor alterações.
5. Não implemente funcionalidades de fases futuras.
6. Não altere arquitetura, stack, regras de negócio ou infraestrutura por iniciativa própria.
7. Se uma solicitação conflitar com este documento, **pare e informe o conflito antes de modificar o código**.
8. Se algo estiver ambíguo e a decisão puder alterar regra de negócio, arquitetura, segurança, banco, custo ou escopo, pergunte ao responsável pelo projeto antes de implementar.
9. Faça a menor alteração correta que resolva a tarefa atual.
10. Ao concluir, informe objetivamente:
   - o que foi alterado;
   - quais arquivos principais foram afetados;
   - quais testes foram executados;
   - se existe algum risco ou pendência.

### Regra de ouro

**Não transformar o Prisma RH em uma demonstração de tecnologias.**

Toda tecnologia deve existir porque resolve um problema real do produto.

---

# 1. VISÃO DO PRODUTO

## Nome

**Prisma RH**

## Definição

O Prisma RH é uma plataforma B2B de gestão, cálculo, conferência e auditoria de folha de pagamento brasileira.

O produto deve permitir que uma organização de RH ou departamento pessoal:

- cadastre e administre empresas clientes;
- administre estabelecimentos/filiais;
- mantenha dados cadastrais e contratuais de funcionários;
- mantenha históricos necessários ao cálculo;
- configure rubricas e parâmetros;
- processe folhas por competência;
- armazene permanentemente os resultados calculados;
- importe dados por CSV/XLSX;
- futuramente integre sistemas externos por API;
- execute regras automáticas de conferência;
- identifique inconsistências;
- acompanhe a análise e resolução das inconsistências;
- mantenha trilha de auditoria;
- consulte indicadores e históricos.

O Prisma RH **não deve depender de outro sistema de folha para funcionar**.

Ele possui seu próprio domínio de folha, seu próprio armazenamento e seu próprio mecanismo de cálculo.

Integrações externas serão complementares.

---

# 2. PROBLEMA EMPRESARIAL

Departamentos pessoais, empresas de BPO/RH e empresas com processamento interno de folha trabalham com:

- grande volume de funcionários;
- diversas empresas e estabelecimentos;
- regras legais que mudam ao longo do tempo;
- lançamentos manuais;
- múltiplos tipos de processamento;
- diferenças entre cadastro, contrato e cálculo;
- risco financeiro;
- risco trabalhista;
- necessidade de conferência antes do fechamento;
- necessidade de explicar posteriormente como determinado valor foi calculado.

O Prisma RH deve reduzir esses riscos oferecendo:

1. cálculo rastreável;
2. armazenamento histórico;
3. conferência automática;
4. workflow de tratamento;
5. parametrização por empresa;
6. auditabilidade.

---

# 3. POSICIONAMENTO DO PRODUTO

O Prisma RH deve parecer um **sistema empresarial real**, e não um projeto acadêmico.

A experiência deve se aproximar de produtos reais de folha e departamento pessoal.

Porém, durante o desenvolvimento e enquanto não houver validação jurídica/fiscal completa:

- não afirmar que o produto é homologado;
- não afirmar conformidade legal absoluta;
- não afirmar que substitui oficialmente um sistema comercial de folha;
- não enviar obrigações oficiais para órgãos públicos sem uma fase específica aprovada;
- não utilizar dados reais de funcionários em ambientes de demonstração.

A meta técnica é reproduzir os cálculos e fluxos brasileiros com o máximo de fidelidade possível.

---

# 4. PRINCÍPIOS FUNDAMENTAIS DO DOMÍNIO

## 4.1 Competência é fundamental

Regras de folha mudam com o tempo.

Nenhum valor legal relevante deve ser assumido como eterno.

Parâmetros como:

- faixas;
- limites;
- percentuais;
- pisos;
- tetos;
- valores de referência;
- regras de incidência;

devem poder ser associados a uma vigência/competência.

### Proibido

Espalhar percentuais legais ou valores anuais diretamente pelo código.

### Esperado

Representar parâmetros versionados por vigência e manter histórico.

---

## 4.2 Cálculo precisa ser explicável

Todo valor calculado deve permitir descobrir:

- qual funcionário;
- qual contrato;
- qual competência;
- qual tipo de folha;
- quais rubricas entraram;
- quais bases foram utilizadas;
- quais parâmetros estavam vigentes;
- qual fórmula/regra foi aplicada;
- qual foi o resultado;
- quando o cálculo ocorreu.

Não aceitar cálculos importantes que produzam apenas um número final sem memória de cálculo.

---

## 4.3 Histórico não deve ser destruído

Uma folha fechada representa um fato histórico.

Alterações cadastrais posteriores não podem reescrever silenciosamente o passado.

Sempre que necessário, utilizar histórico/vigência para:

- salário;
- lotação;
- cargo;
- jornada;
- parâmetros;
- regras;
- rubricas;
- dados contratuais relevantes.

---

## 4.4 Reprocessamento deve ser controlado

Folhas poderão ser recalculadas antes do fechamento.

Depois do fechamento:

- alterações devem obedecer fluxo explícito;
- histórico deve permanecer auditável;
- nenhum resultado deve desaparecer sem registro.

---

# 5. ESTRUTURA MULTIEMPRESA

A hierarquia inicial do Prisma RH será:

```text
Organização
├── Usuários
├── Empresa A
│   ├── Estabelecimento 1
│   ├── Estabelecimento 2
│   └── Funcionários
├── Empresa B
│   ├── Estabelecimento 1
│   └── Funcionários
└── Empresa C
```

## Organização

É o tenant do Prisma RH.

Exemplos:

- escritório de BPO/RH;
- grupo empresarial;
- departamento pessoal centralizado.

## Empresa

Representa uma empresa administrada pela organização.

## Estabelecimento

Representa filial/unidade da empresa quando aplicável.

## Regra de isolamento

Dados de uma organização jamais podem ser acessados por outra organização.

O isolamento deve existir no backend e no banco, e nunca depender apenas do frontend.

Toda consulta de dado empresarial deverá considerar o contexto da organização autenticada.

Nunca confiar em um `IdOrganizacao` recebido do navegador sem validar que o usuário autenticado possui acesso a ele.

---

# 6. PERFIS DE ACESSO

Perfis iniciais:

## Administrador da Plataforma

Uso restrito à administração técnica do Prisma RH.

Pode:

- administrar organizações;
- executar operações administrativas globais autorizadas;
- acessar ferramentas técnicas previstas para a plataforma.

Não deve ser utilizado como usuário comum de uma organização.

## Administrador da Empresa

Pode administrar o ambiente da organização:

- empresas;
- estabelecimentos;
- usuários;
- permissões;
- parâmetros;
- configurações empresariais.

## Analista de RH

Pode:

- manter cadastros autorizados;
- importar dados;
- processar folha;
- executar análises;
- tratar inconsistências;
- justificar ocorrências.

## Auditor

Pode:

- consultar folhas;
- consultar inconsistências;
- consultar memória de cálculo;
- consultar histórico e auditoria.

Por padrão, não altera dados operacionais.

## Visualizador

Possui acesso somente de leitura aos módulos explicitamente liberados.

### Regra

Autorização deve ser aplicada no backend.

Ocultar botão no frontend **não é mecanismo de autorização**.

---

# 7. DOMÍNIOS FUNCIONAIS PREVISTOS

O produto será construído progressivamente.

Os módulos previstos são:

1. Identidade e acesso
2. Organizações
3. Empresas
4. Estabelecimentos
5. Funcionários
6. Contratos de trabalho
7. Dependentes
8. Históricos contratuais
9. Rubricas
10. Parâmetros de cálculo
11. Competências
12. Folhas de pagamento
13. Lançamentos
14. Motor de cálculo
15. Memória de cálculo
16. Importações
17. Motor de análises
18. Inconsistências
19. Workflow de tratamento
20. Auditoria
21. Indicadores e dashboards
22. Integrações externas
23. Processamento assíncrono
24. Infraestrutura de produção

A existência desta lista **não autoriza implementar todos os módulos agora**.

O roadmap deste documento determina quando cada módulo poderá ser criado.

---

# 8. ESCOPO DE FOLHA

O objetivo final é suportar progressivamente cenários comuns da folha brasileira.

## Tipos de processamento previstos

- mensal;
- adiantamento, se aprovado para a fase correspondente;
- férias;
- 13º salário;
- rescisão;
- processamentos complementares quando aprovados.

## Dados e cálculos previstos ao longo do projeto

Exemplos:

- salário contratual;
- salário proporcional;
- dias trabalhados;
- horas;
- adicionais;
- descontos;
- faltas;
- benefícios;
- bases de cálculo;
- INSS;
- FGTS;
- IRRF quando aplicável;
- férias;
- 1/3 de férias;
- 13º salário;
- rescisão;
- afastamentos;
- dependentes;
- pensão;
- outras rubricas parametrizadas.

### Importante

Esta lista expressa o destino do produto.

Cada cálculo só pode ser implementado quando:

1. a fase correspondente estiver aprovada;
2. a regra tiver sido especificada;
3. houver fonte confiável para a regra;
4. os parâmetros forem modelados de maneira versionável;
5. existirem testes para os principais cenários e limites.

---

# 9. RUBRICAS E LANÇAMENTOS

Rubricas representam eventos de folha.

Exemplos conceituais:

- salário;
- hora extra;
- adicional;
- benefício;
- desconto;
- contribuição;
- férias;
- 13º.

Cada rubrica deverá poder possuir metadados necessários para o cálculo, como:

- natureza;
- tipo: provento/desconto/informativo;
- vigência;
- fórmula ou estratégia de cálculo;
- incidências;
- prioridade/ordem quando necessária;
- situação ativa/inativa.

Não criar um mecanismo genérico de execução de código arbitrário pelo usuário.

Parametrização não deve permitir executar scripts fornecidos pelo usuário.

---

# 10. MOTOR DE CÁLCULO

O motor de cálculo é um dos componentes centrais do Prisma RH.

Princípios:

- determinístico;
- testável;
- rastreável;
- versionável;
- separado da camada HTTP;
- independente da interface;
- sem dependência direta de AWS;
- sem acessar banco indiscriminadamente durante cada operação matemática;
- não esconder regra de negócio em controller.

O motor deverá evoluir de forma incremental.

Não construir antecipadamente um "motor universal" para todos os tipos de folha.

Começar pelo menor conjunto de regras aprovado para a fase atual.

---

# 11. MOTOR DE ANÁLISES

Depois de uma folha calculada ou importada, regras de análise poderão procurar inconsistências.

Estratégia escolhida:

**regras oficiais do sistema + parâmetros configuráveis por organização/empresa.**

O usuário não poderá inicialmente escrever código ou SQL para criar regras.

Exemplos conceituais de análises:

- salário calculado diferente do esperado;
- funcionário desligado recebendo rubrica incompatível;
- benefício indevido;
- base de encargo incompatível;
- lançamento duplicado;
- divergência cadastral;
- valor fora de tolerância;
- funcionário ausente da folha;
- rubrica incompatível com situação contratual.

Cada resultado deve registrar:

- regra executada;
- versão da regra;
- folha;
- funcionário quando aplicável;
- valores relevantes;
- descrição;
- severidade;
- data da execução;
- status de tratamento.

---

# 12. WORKFLOW DE INCONSISTÊNCIAS

Fluxo padrão:

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

Transições deverão ser explícitas e auditáveis.

Uma inconsistência deverá possuir, conforme evolução do produto:

- responsável;
- comentários;
- justificativa;
- evidências;
- histórico de status;
- timestamps;
- usuário que realizou cada alteração.

Não apagar o histórico ao alterar o status.

---

# 13. IMPORTAÇÃO DE DADOS

Formatos iniciais previstos:

- CSV;
- XLSX.

A importação deve evoluir em etapas.

Ela poderá ser usada para:

- funcionários;
- dados contratuais;
- lançamentos;
- referências necessárias ao cálculo;
- folhas vindas de sistemas externos quando houver integração/migração.

## Regras obrigatórias

- validar extensão;
- validar conteúdo;
- limitar tamanho;
- nunca confiar no nome do arquivo;
- não executar macros;
- fornecer erros de importação compreensíveis;
- não deixar uma importação parcialmente inválida corromper a base;
- manter rastreabilidade da origem dos dados.

---

# 14. INTEGRAÇÕES EXTERNAS

O Prisma RH deverá ser capaz, futuramente, de integrar ERPs, HRIS ou outros sistemas de folha por API.

A integração externa é **opcional** e não é requisito para o funcionamento central do produto.

Arquitetura futura deverá permitir adaptadores por provedor sem contaminar o domínio.

Exemplo conceitual:

```text
Prisma RH
    |
    +-- IntegracaoSistemaA
    +-- IntegracaoSistemaB
    +-- IntegracaoGenericaApi
```

## Regra de custo

O custo de uma API de terceiro não faz parte do orçamento AWS.

Antes de integrar qualquer sistema externo:

- verificar se existe API oficial;
- verificar autenticação;
- verificar limites;
- verificar custo;
- verificar termos de uso;
- obter autorização explícita para implementar.

## AWS

Para integrações HTTP públicas simples, priorizar arquitetura serverless e evitar colocar Lambda em VPC apenas para acessar a internet.

**Não criar NAT Gateway sem autorização explícita**, devido ao custo fixo.

---

# 15. STACK OFICIAL

## Backend

- **C#**
- **.NET 10**
- ASP.NET Core Web API
- Entity Framework Core
- PostgreSQL
- OpenAPI

## Frontend

- React
- TypeScript
- Vite
- Tailwind CSS
- shadcn/ui

## Banco

### Desenvolvimento

PostgreSQL via Docker Compose.

### Produção

Neon PostgreSQL no plano gratuito, enquanto atender ao projeto.

O banco da produção não deve depender do crédito AWS.

## Testes backend

- xUnit
- testes unitários para regras de negócio;
- testes de integração quando necessários;
- Testcontainers/PostgreSQL apenas quando a fase justificar.

## Testes frontend

- Vitest
- Testing Library
- Playwright para fluxos E2E quando a aplicação estiver suficientemente estável.

## Infraestrutura AWS prevista

Somente quando chegar às fases correspondentes:

- AWS Lambda;
- API Gateway HTTP API;
- S3;
- SQS;
- CloudWatch;
- eventualmente EventBridge/Step Functions se surgir necessidade real e houver aprovação.

## Hospedagem

### Frontend

Vercel, enquanto o plano gratuito for adequado à demo pessoal.

### Backend

AWS Lambda + API Gateway HTTP API na fase de produção.

### Arquivos

AWS S3 na fase de produção.

---

# 16. LIMITE DE CUSTO AWS

O Prisma RH faz parte de um portfólio com cinco projetos novos compartilhando os créditos da conta AWS.

## Limite absoluto do Prisma RH

**US$ 6,50 por mês em serviços AWS.**

Esse valor é teto, não meta de gasto.

A preferência é manter o projeto próximo de US$ 0 durante uso normal de portfólio.

## Regras obrigatórias

O agente NÃO pode, sem autorização explícita:

- criar EC2;
- criar RDS;
- criar ECS/Fargate;
- criar EKS;
- criar Load Balancer;
- criar NAT Gateway;
- reservar IP público pago;
- criar OpenSearch;
- criar ElastiCache;
- criar recursos com cobrança fixa relevante;
- aumentar capacidade provisionada;
- criar qualquer recurso AWS cujo custo mensal provável seja desconhecido.

Preferir:

- serverless;
- pay-per-use;
- scale-to-zero;
- free tiers;
- serviços externos gratuitos quando tecnicamente adequados.

Todo recurso AWS deve usar tags de custo:

```text
Projeto = PrismaRH
Ambiente = dev | homologacao | producao
GerenciadoPor = IaC | manual
```

## Antes de deploy AWS

O agente deve:

1. informar quais recursos serão criados/alterados;
2. indicar risco de custo;
3. aguardar autorização explícita;
4. somente então executar o deploy.

---

# 17. ARQUITETURA DE ALTO NÍVEL

## Durante desenvolvimento local

```text
React
  |
  v
ASP.NET Core Web API
  |
  v
PostgreSQL
```

## Produção planejada

```text
                  Vercel
                    |
               React + TS
                    |
                    v
             API Gateway HTTP
                    |
                    v
               Lambda .NET
               /         \
              v           v
    Neon PostgreSQL      S3
                          |
                          v
                         SQS
                          |
                          v
                   Lambda Worker
                          |
                          v
                  Neon PostgreSQL
```

Esta arquitetura final **não deve ser criada antecipadamente**.

---

# 18. ESTILO ARQUITETURAL

Começar como **monólito modular**.

Não iniciar com microserviços.

Separar responsabilidades de maneira clara, sem criar camadas vazias apenas para parecer "enterprise".

Direção conceitual:

```text
backend/
├── PrismaRH.Api
├── PrismaRH.Aplicacao
├── PrismaRH.Dominio
├── PrismaRH.Infraestrutura
└── testes/
```

Essa estrutura pode ser ajustada quando a implementação real justificar, mas não deve ser alterada sem autorização.

## Dependências

Direção desejada:

```text
Api
  ↓
Aplicacao
  ↓
Dominio

Infraestrutura → Aplicacao/Dominio conforme contratos necessários
```

O domínio não deve depender de:

- ASP.NET;
- Entity Framework;
- AWS SDK;
- React;
- detalhes de infraestrutura.

---

# 19. IDIOMA DO PROJETO

Decisão explícita do projeto:

**Código e produto em português.**

## Código

Usar nomes de domínio em português:

```csharp
Funcionario
Empresa
Estabelecimento
ContratoTrabalho
Competencia
FolhaPagamento
Rubrica
LancamentoFolha
RegraAnalise
Inconsistencia
MemoriaCalculo
```

Identificadores de código devem ser escritos sem acentos.

Exemplo:

```csharp
public class Funcionario
{
    public Guid Id { get; private set; }
    public string Nome { get; private set; }
}
```

Evitar:

```csharp
Employee
Payroll
Company
AnalysisRule
```

quando o conceito pertence ao domínio Prisma RH.

Nomes impostos por frameworks, bibliotecas e protocolos externos permanecem com sua nomenclatura oficial.

## API

Preferir rotas em português:

```text
/api/empresas
/api/funcionarios
/api/folhas
/api/inconsistencias
```

## Banco

Nomes de tabelas e colunas de domínio devem seguir o padrão em português definido pelo projeto.

## Interface

100% em português do Brasil inicialmente.

Internacionalização não faz parte do escopo inicial.

---

# 20. PADRÕES DE CÓDIGO C#

- Nullable Reference Types habilitado.
- Async/await para I/O.
- CancellationToken onde fizer sentido em operações assíncronas.
- Não usar `dynamic` para contornar modelagem.
- Não usar `object` como solução genérica para regras de domínio.
- Controllers devem ser finos.
- Regras de negócio não ficam em controllers.
- Entidades devem proteger invariantes relevantes.
- Evitar setters públicos indiscriminados.
- Evitar métodos gigantes.
- Preferir nomes explícitos a abreviações.
- Não criar abstrações sem uso real.
- Não aplicar Repository genérico automaticamente.
- Não aplicar Unit of Work customizado se EF Core já resolver a necessidade.
- Não adicionar MediatR/CQRS apenas por estética arquitetural.
- Não adicionar AutoMapper automaticamente.
- Não adicionar bibliotecas sem necessidade demonstrável.

---

# 21. PADRÕES FRONTEND

- React funcional.
- TypeScript estrito.
- Evitar `any`.
- Componentes devem ter responsabilidade clara.
- Regras críticas de negócio pertencem ao backend.
- Frontend pode validar UX, mas backend deve repetir validações de segurança e negócio necessárias.
- Não duplicar estados desnecessariamente.
- Estados de carregamento, vazio e erro devem existir nas telas relevantes.
- Acessibilidade básica é obrigatória.
- Interface deve parecer software B2B profissional.
- Evitar visual excessivamente "landing page".
- Priorizar clareza, densidade adequada e produtividade operacional.

---

# 22. BANCO DE DADOS

PostgreSQL é a fonte de verdade dos dados relacionais.

Princípios:

- migrations versionadas;
- constraints no banco quando apropriado;
- foreign keys;
- índices baseados em consultas reais;
- timestamps;
- isolamento por organização;
- não usar soft delete automaticamente em todas as tabelas;
- históricos relevantes devem ser modelados explicitamente;
- dinheiro nunca deve usar `float`/`double`.

## Valores monetários

No C#:

```csharp
decimal
```

No PostgreSQL:

```text
numeric/decimal
```

Precisão deve ser definida conscientemente para cada uso.

---

# 23. DATAS E TEMPO

Folha depende fortemente de datas.

Regras:

- diferenciar data civil de instante;
- competência deve possuir tipo próprio/modelagem explícita;
- não representar competência apenas como string solta em todo o sistema;
- armazenar timestamps técnicos de forma consistente;
- timezone da interface inicial: Brasil, conforme configuração do produto;
- não assumir que `DateTime.Now` é apropriado em qualquer lugar;
- preferir abstração de relógio em regras que dependem do momento atual.

---

# 24. SEGURANÇA

Requisitos progressivos:

- senhas nunca em texto puro;
- JWT de curta duração;
- refresh token com rotação quando implementado;
- autorização no backend;
- isolamento multiempresa;
- validação de input;
- proteção de upload;
- rate limiting em endpoints sensíveis quando chegar à fase adequada;
- CORS restritivo em produção;
- secrets fora do repositório;
- logs sem dados sensíveis desnecessários;
- auditoria de operações relevantes.

## Dados pessoais

A demo pública deve utilizar **somente dados fictícios**.

Nunca:

- CPF real;
- salário real;
- dado médico real;
- informação de funcionário real;
- dado de cliente real;
- credenciais reais.

Dados fictícios devem ser claramente identificáveis como demonstração quando necessário.

---

# 25. LGPD E PRIVACIDADE

O projeto lida conceitualmente com dados pessoais e trabalhistas.

Mesmo sendo um portfólio, a arquitetura deve demonstrar boas práticas:

- minimização de dados;
- segregação;
- controle de acesso;
- auditabilidade;
- não exposição desnecessária;
- retenção consciente;
- dados fictícios em demo.

Não implementar alegações de "conformidade LGPD completa" sem avaliação específica.

---

# 26. LOGS E AUDITORIA

Diferenciar:

## Log técnico

Usado para operação e diagnóstico.

Exemplos:

- request;
- erro;
- duração;
- correlation id;
- falha de integração.

## Auditoria de negócio

Usada para responder:

- quem alterou;
- o que alterou;
- quando alterou;
- em qual organização/empresa;
- qual era o contexto.

Não usar log técnico como substituto de auditoria de negócio.

---

# 27. TESTES

Testes são parte da implementação, principalmente no cálculo.

## Obrigatório para regras de folha

Cada regra importante deve possuir testes cobrindo:

- caso normal;
- limites;
- zero;
- arredondamento quando aplicável;
- datas relevantes;
- cenário inválido;
- mudanças de parâmetros/vigência quando aplicável.

## Regra crítica

Uma alteração em cálculo de folha não está concluída se alterar comportamento sem atualizar/adicionar os testes correspondentes.

Não perseguir porcentagem de coverage artificial.

Cobrir comportamento importante.

---

# 28. ARREDONDAMENTO E PRECISÃO

Nunca inventar estratégia de arredondamento.

Toda regra que envolva arredondamento deve definir:

- em qual etapa arredondar;
- quantidade de casas;
- modo de arredondamento;
- se arredonda base, parcela ou resultado.

O comportamento deve possuir teste.

---

# 29. FONTES PARA REGRAS LEGAIS

Quando uma regra brasileira depender de legislação, tabela ou parâmetro oficial:

Priorizar fontes oficiais.

Exemplos de categorias de fonte:

- Governo Federal;
- Receita Federal;
- eSocial;
- INSS/Previdência;
- Caixa/FGTS;
- legislação publicada oficialmente.

Não usar artigo de blog como única fonte para regra crítica.

Ao implementar regra legal:

1. registrar a fonte na documentação apropriada;
2. registrar vigência;
3. transformar números variáveis em parâmetros versionados;
4. criar testes.

O `CLAUDE.md` não deve ser atualizado com números anuais apenas para refletir uma tabela nova.
Os dados versionados devem viver no mecanismo apropriado do sistema.

---

# 30. GIT

Permitido:

- inspecionar histórico;
- criar branch quando solicitado;
- preparar alterações;
- gerar diff;
- executar testes;
- criar commit local se a tarefa explicitamente pedir.

Sem autorização explícita:

- não executar `git push`;
- não fazer force push;
- não alterar histórico publicado;
- não apagar branches remotas;
- não fazer merge em branch protegida;
- não publicar release.

Commits devem ser pequenos e coerentes.

Idioma das mensagens de commit: português.

---

# 31. DEPLOY

Claude Code e Codex **podem realizar deploy somente quando houver autorização explícita na tarefa atual**.

Autorização antiga não vale automaticamente para novos deploys.

Antes de cada deploy:

1. informar ambiente;
2. informar recursos afetados;
3. informar migrations;
4. informar impacto esperado;
5. informar risco de custo;
6. executar apenas após autorização.

Sem autorização:

**não executar deploy.**

---

# 32. AÇÕES DESTRUTIVAS

Proibido sem autorização explícita:

- apagar banco;
- dropar schema;
- resetar produção;
- apagar bucket;
- apagar fila;
- destruir stack;
- excluir recursos cloud;
- apagar migrations já aplicadas;
- apagar dados para "resolver" erro;
- executar comando de cleanup destrutivo;
- alterar histórico Git.

Se um teste necessitar reset de banco local efêmero, usar ambiente isolado criado especificamente para teste.

---

# 33. SEGREDOS

Nunca:

- commitar `.env`;
- imprimir secret completo;
- imprimir token completo;
- imprimir senha;
- colocar chave real em documentação;
- copiar credencial de produção para teste.

Usar:

- `.env.example`;
- secrets do ambiente;
- GitHub Secrets;
- mecanismos seguros da plataforma de deploy.

---

# 34. O QUE OS AGENTES PODEM FAZER

Sem autorização adicional, dentro da fase atual e da tarefa recebida:

- ler arquivos;
- pesquisar o código;
- criar arquivos necessários;
- editar arquivos;
- refatorar estritamente o necessário para a tarefa;
- executar build;
- executar testes;
- executar lint;
- executar aplicação local;
- usar Docker local;
- consultar logs locais;
- criar migration necessária à tarefa;
- atualizar documentação relacionada à alteração.

---

# 35. O QUE OS AGENTES NÃO PODEM DECIDIR SOZINHOS

Exige aprovação:

- trocar linguagem;
- trocar framework;
- trocar banco;
- trocar ORM;
- adicionar microserviço;
- adicionar fila;
- adicionar cache;
- adicionar Redis;
- adicionar RabbitMQ;
- adicionar Kafka;
- adicionar CQRS;
- adicionar Event Sourcing;
- adicionar Kubernetes;
- adicionar novo provedor cloud;
- alterar estratégia de autenticação;
- alterar multi-tenancy;
- alterar regra legal;
- alterar regra de cálculo;
- ampliar escopo;
- implementar fase futura;
- criar recurso AWS;
- fazer deploy;
- publicar código;
- adicionar IA/LLM ao produto;
- introduzir custo recorrente.

---

# 36. TECNOLOGIAS EXPRESSAMENTE FORA DO ESCOPO INICIAL

Não adicionar sem necessidade comprovada e autorização:

- Kubernetes;
- Kafka;
- RabbitMQ;
- Redis;
- Elasticsearch/OpenSearch;
- GraphQL;
- gRPC;
- Event Sourcing;
- microserviços;
- service mesh;
- WebSockets;
- IA generativa;
- banco vetorial.

O Prisma RH poderá usar algumas dessas tecnologias no futuro **somente se o produto criar uma necessidade real**.

---

# 37. ROADMAP OBRIGATÓRIO

O roadmap é sequencial.

Uma fase futura não deve ser implementada porque "será necessária depois".

---

## FASE 0 — Fundação do repositório

Objetivo:

Criar uma base simples, reproduzível e saudável.

Entregas:

- monorepo;
- backend .NET 10;
- frontend React + TypeScript + Vite;
- PostgreSQL local via Docker Compose;
- configuração de desenvolvimento;
- `.env.example`;
- health check;
- build backend;
- build frontend;
- testes mínimos de infraestrutura do projeto;
- README inicial com execução local.

Não implementar regra de folha nesta fase.

---

## FASE 1 — Identidade e multiempresa

Entregas:

- Organização;
- Usuário;
- autenticação;
- perfis;
- autorização;
- Empresa;
- Estabelecimento;
- isolamento por organização;
- testes de isolamento.

Perfis:

- Administrador da Plataforma;
- Administrador da Empresa;
- Analista de RH;
- Auditor;
- Visualizador.

---

## FASE 2 — Cadastro funcional de RH

Entregas progressivas:

- Funcionário;
- Contrato de trabalho;
- histórico salarial;
- lotação/estabelecimento;
- dados necessários ao primeiro cálculo;
- dependentes quando necessários ao cálculo aprovado;
- telas operacionais correspondentes.

Não cadastrar campos sem uso claro apenas para "parecer completo".

---

## FASE 3 — Núcleo da folha mensal

Objetivo:

Calcular a primeira folha mensal de maneira rastreável.

Entregas:

- Competência;
- Rubrica;
- parâmetros por vigência;
- FolhaPagamento;
- lançamentos;
- motor de cálculo inicial;
- memória de cálculo;
- armazenamento do resultado;
- reprocessamento controlado;
- testes das primeiras regras.

Escopo exato das primeiras rubricas deverá ser aprovado antes da implementação.

---

## FASE 4 — Evolução dos cálculos brasileiros

Adicionar progressivamente, após especificação e testes:

- incidências;
- encargos;
- descontos legais;
- férias;
- 13º;
- rescisão;
- afastamentos;
- demais eventos aprovados.

Não implementar tudo em uma única tarefa.

Cada bloco deve ser validado antes do próximo.

---

## FASE 5 — Importações CSV/XLSX

Entregas:

- importação segura;
- preview;
- validação;
- relatório de erros;
- processamento transacional apropriado;
- rastreabilidade;
- modelos de arquivo;
- testes.

Inicialmente processamento local/síncrono quando o volume permitir.

---

## FASE 6 — Motor de análises

Entregas:

- catálogo de regras;
- regras versionadas;
- parametrização permitida por empresa;
- execução por folha;
- resultados;
- severidade;
- tolerância quando aplicável;
- histórico de execução;
- testes.

---

## FASE 7 — Workflow e auditoria

Entregas:

- Detectada;
- Em análise;
- Justificada;
- Corrigida;
- Resolvida;
- responsável;
- comentários;
- evidências;
- histórico;
- auditoria de negócio;
- dashboard operacional.

---

## FASE 8 — Integrações por API

Somente após o núcleo do Prisma RH funcionar sozinho.

Entregas serão aprovadas por integração.

Criar contratos/adaptadores sem acoplar o domínio ao fornecedor.

Nenhuma integração paga deve ser adicionada sem aprovação.

---

## FASE 9 — Processamento assíncrono AWS

Somente quando houver um caso real de processamento que justifique.

Arquitetura prevista:

```text
Upload
  ↓
API
  ↓
S3
  ↓
SQS
  ↓
Lambda Worker .NET
  ↓
Processamento
  ↓
PostgreSQL
```

Requisitos:

- idempotência;
- retry;
- DLQ se necessária;
- status de processamento;
- correlation id;
- logs;
- tratamento de erro;
- limite de custo.

---

## FASE 10 — Produção

Entregas:

- frontend Vercel;
- API Gateway HTTP API;
- Lambda .NET;
- Neon PostgreSQL;
- S3/SQS se já aprovados;
- CloudWatch;
- CI/CD;
- migrations seguras;
- dados fictícios de demonstração;
- segurança;
- documentação;
- orçamento/alertas AWS.

A aplicação deve permanecer demonstrável publicamente.

---

# 38. DADOS DE DEMONSTRAÇÃO

A demo deverá possuir dados 100% fictícios.

Criar uma organização fictícia, por exemplo:

```text
Prisma Serviços de RH Ltda.
```

e empresas fictícias administradas por ela.

Dados não devem reproduzir pessoas reais.

A demo deverá permitir que recrutadores entendam rapidamente:

1. empresas;
2. funcionários;
3. folha;
4. cálculo;
5. inconsistências;
6. workflow;
7. auditoria.

---

# 39. DEFINITION OF DONE

Uma tarefa só está concluída quando, dentro do escopo aplicável:

- implementação atende à regra;
- build passa;
- testes relevantes passam;
- não há erro conhecido ignorado;
- migrations estão coerentes;
- segurança não foi enfraquecida;
- multi-tenancy foi respeitada;
- idioma do domínio permanece português;
- documentação afetada foi atualizada;
- nenhuma tecnologia não autorizada foi adicionada;
- nenhuma fase futura foi implementada;
- nenhuma credencial foi exposta;
- não houve deploy sem autorização;
- o agente consegue explicar objetivamente o que mudou.

---

# 40. CHECKLIST ANTES DE ALTERAR CÓDIGO

Antes de editar, responda internamente:

- [ ] Li o `CLAUDE.md`?
- [ ] Sei qual é a fase atual?
- [ ] A tarefa pertence a essa fase?
- [ ] Inspecionei a implementação existente?
- [ ] Existe regra de negócio envolvida?
- [ ] Existe impacto multiempresa?
- [ ] Existe impacto de segurança?
- [ ] Existe impacto no banco?
- [ ] Existe impacto financeiro na AWS?
- [ ] Estou adicionando tecnologia não aprovada?
- [ ] Estou tentando resolver algo que não foi pedido?

Se qualquer resposta indicar risco ou conflito, interromper e pedir decisão.

---

# 41. CHECKLIST APÓS ALTERAR CÓDIGO

- [ ] Build backend executado quando aplicável.
- [ ] Testes backend executados quando aplicável.
- [ ] Build frontend executado quando aplicável.
- [ ] Testes frontend executados quando aplicável.
- [ ] Migration revisada quando aplicável.
- [ ] Nenhum secret exposto.
- [ ] Nenhum dado real incluído.
- [ ] Nenhum recurso AWS criado sem autorização.
- [ ] Nenhum deploy realizado sem autorização.
- [ ] Alteração ficou restrita à tarefa.
- [ ] Resultado final foi explicado ao responsável.

---

# 42. FILOSOFIA DE IMPLEMENTAÇÃO

O Prisma RH deve crescer assim:

```text
correto
   ↓
simples
   ↓
testável
   ↓
explicável
   ↓
seguro
   ↓
observável
   ↓
escalável quando necessário
```

e não assim:

```text
complexo
   ↓
"enterprise"
   ↓
muitas tecnologias
   ↓
tentativa de encontrar um problema para justificá-las
```

---

# 43. OBJETIVO DE APRENDIZADO

Este projeto também existe para que seu autor domine o que construiu.

Por isso:

- evitar código mágico;
- evitar arquitetura copiada sem compreensão;
- explicar decisões;
- manter regras legíveis;
- preferir soluções que possam ser defendidas tecnicamente;
- não esconder complexidade importante atrás de abstrações desnecessárias.

Quando houver duas soluções corretas, favorecer a que tornar o domínio mais compreensível sem sacrificar requisitos reais.

---

# 44. AUTORIDADE DESTE DOCUMENTO

Este documento é a fonte de verdade permanente do projeto.

Prioridade:

1. solicitação explícita atual do responsável pelo projeto;
2. este `CLAUDE.md`;
3. documentação técnica específica aprovada;
4. código existente;
5. sugestões do agente.

Uma solicitação atual que altere uma decisão permanente deve resultar também na atualização deste arquivo ou da documentação de domínio correspondente.

O agente nunca deve assumir que uma decisão permanente mudou apenas porque encontrou código diferente.

---

**Fim do documento.**
