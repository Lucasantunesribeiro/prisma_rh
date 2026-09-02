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

## 0.1 MODO DE EXECUÇÃO — UMA AUTORIZAÇÃO DE FASE VALE PARA A FASE INTEIRA

> **Decisão do responsável, registrada em 30/08/2026.** Substitui o modo anterior, em que
> cada etapa de uma fase era apresentada e aprovada separadamente.

Quando o responsável autoriza uma fase, ele está autorizando **executá-la até o fim**. O
agente planeja as etapas, implementa, testa, corrige e conclui — e só então apresenta **um
relatório final**.

### O que o agente faz sozinho, dentro da fase autorizada

Planejar as etapas · implementar · pesquisar documentação quando precisar · tomar decisões
técnicas normais · criar migrations · escrever e executar testes · corrigir testes que
falham · rodar integração contra PostgreSQL real · executar os Security Gates · lint ·
builds · revisar migrations · revisar o `git diff` · procurar regressões · atualizar a
documentação · fazer commits locais coerentes.

### O que o agente NÃO interrompe para perguntar

Escolha interna de implementação · organização de classes · testes adicionais · refactor
pequeno que a tarefa exige · **defeito encontrado durante a fase** · biblioteca já
aprovada · qualquer decisão reversível que respeite este documento e o `ROADMAP.md`.

Nesses casos: investigar, decidir, implementar, testar e seguir.

### O que ainda interrompe

| Situação | Por quê |
|---|---|
| **Conflito real entre fontes oficiais** sobre a mesma regra legal ou fiscal | Escolher uma por interpretação própria seria inventar direito. Mostrar as duas fontes é obrigatório (`§29`). |
| **Mudar arquitetura ou stack aprovada** | `§35`. |
| **Ação destrutiva irreversível** | `§32`. |
| **Segredo ou credencial que o agente não possui** | `§33`. |
| **Criar infraestrutura paga** | `§16`. |
| **Deploy** | `§31`. |
| **`git push`** | `§30`. |
| **Custo AWS relevante** | `§16`. |
| **Requisito de negócio impossível de determinar com segurança** | Adivinhar regra de folha é pior que perguntar. |

Fora dessa lista, não interromper.

### Testes fazem parte da implementação

Não é aceitável entregar a fase e só então descobrir que falta testar. Dentro da própria
fase: **implementar → testar → achar defeito → corrigir → testar de novo → seguir**.

### Verificação obrigatória no fim de toda fase

Suíte backend completa · testes frontend completos · testes repetidos quando houver
concorrência ou intermitência · lint · build backend · build frontend · integração contra
PostgreSQL real quando aplicável · verificação de migrations · Security Gate final ·
revisão do diff · varredura de segredos · `git status` limpo.

Uma fase só está concluída quando **todos** os critérios dela estiverem atendidos.

### Ao terminar

Commits locais feitos · `ROADMAP.md`, `README.md` e `CLAUDE.md` atualizados quando
pertinente · **um** relatório final da fase · e **parar antes da fase seguinte**.

Continua proibido sem autorização explícita naquela mensagem: `git push`, deploy, recurso
AWS, ação destrutiva e antecipar fase futura.

---

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
- não utilizar dados reais de funcionários em ambientes de demonstração;
- não anunciar recursos de IA como existentes: são planejados para a Fase 11 (§37).

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
25. Assistente inteligente (camada de IA, a partir da Fase 11 — ver §37)

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

> ### ⚠️ Decisão do responsável, registrada em 31/08/2026 — substitui a preferência acima
>
> **Zero custo AWS é requisito arquitetural do portfólio; serviços pagos por
> existência não são permitidos.**
>
> O teto de US$ 6,50 continua valendo como limite de contrato, mas **deixou de ser
> o alvo**. O alvo é **US$ 0,00 previsto**, e ele é critério de projeto, não de
> economia: um serviço que cobra só por existir — sem ninguém usar o sistema — não
> entra na arquitetura, e a solução é **redesenhada** em vez de aprovada com
> ressalva.
>
> **O que a decisão exclui, e por quê:**
>
> | Excluído | Motivo |
> |---|---|
> | **S3** | Ausente da tabela oficial de Free Tier permanente da AWS: cobra desde o primeiro byte |
> | **API Gateway** | Idem. A Lambda Function URL cobre o caso, dentro da franquia da própria Lambda |
> | **KMS customer-managed** | US$ 1,00/mês por chave, só por existir. Filas usam a criptografia própria da SQS, com chave da AWS |
> | **NAT Gateway** | ~US$ 32/mês fixos — sozinho, cinco vezes o teto do projeto |
> | **EC2 · RDS · ECS/Fargate · EKS · ALB** | Cobrança por hora provisionada, independente de uso |
> | **Provisioned concurrency** | Mantém execução quente por hora. Não confundir com *reserved concurrency*, que é gratuita |
> | **Provisioned mode no event source mapping** | Pollers dedicados cobrados por hora |
>
> **O que a decisão exige de quem implementa:** o contexto por trás é que a conta
> perdeu o plano gratuito — habilitar o IAM Identity Center criou uma AWS
> Organizations, e isso dispara o upgrade automático documentado. Os US$ 100 de
> crédito viraram US$ 0,00, e **Free Tier não é teto de gasto**: passar da franquia
> não bloqueia nada, apenas cobra.
>
> Por isso não basta "provavelmente cabe". Cada franquia precisa de um **limite
> técnico** que torne a ultrapassagem improvável — memória mínima, timeout curto,
> concorrência reservada, long polling, retenção de log, teto de retentativas. Os
> números vivem em `OrcamentoSemCusto`, cada um com a conta escrita ao lado.
>
> **Antes de criar qualquer recurso novo:** consultar o preço vigente e confirmar
> que não há cobrança por existência. Se houver, não criar — redesenhar.

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

# 24. MODELO DE SEGURANÇA

> Seção reescrita em **27/08/2026**. Antes era uma lista de doze requisitos progressivos.
> Todos eles continuam valendo e estão distribuídos abaixo, agora com o **porquê** de cada
> um e com as decisões já implementadas registradas como permanentes.
>
> **Segurança não é uma fase.** Toda fase do roadmap tem um **Security Gate**
> obrigatório, definido em `ROADMAP.md §4.1`. A fase de Hardening continua existindo como
> auditoria final e fortalecimento — não como o momento em que segurança começa.

## 24.1 O que este documento promete, e o que não promete

**O Prisma RH nunca afirma ser "100% seguro" nem invulnerável.** Nenhum sistema pode
garantir isso, e prometer é enganar quem lê.

O objetivo é mensurável e honesto:

1. reduzir a superfície de ataque;
2. eliminar vulnerabilidades de classes conhecidas;
3. detectar comportamento anormal;
4. dificultar exploração, para que um erro isolado não baste;
5. permitir resposta e recuperação quando algo der errado.

## 24.2 Princípios permanentes

**Secure by default** — o padrão de qualquer coisa nova é o mais restrito. Endpoint nasce
autenticado, arquivo nasce privado, permissão nasce negada. Abrir exige decisão registrada.

**Least privilege** — cada perfil, credencial de pipeline, papel IAM e usuário de banco
recebe o mínimo. "Admin porque é mais fácil" não é justificativa.

**Defense in depth** — nenhum controle é o único. Errar um não deve vazar.

**Fail closed** — na dúvida, negar. Sem usuário autenticado o `IdOrganizacao` é
`Guid.Empty`, que não casa com nada.

**Zero trust nas fronteiras** — navegador→API, API→banco, API→provedor externo, job→dados,
IA→dados. Cada fronteira valida por conta própria. Nada é confiável por "vir de dentro".

**Rastreabilidade · Segregação multiempresa · Minimização de dados · Proteção contra abuso
· Recuperação segura · Supply chain · Segurança contínua** — detalhados adiante e no gate
do `ROADMAP.md §4.1`.

## 24.3 Identidade

### Decisões implementadas — permanentes

Estas já existem no código e **não devem ser trocadas para "modernizar"**. Cada uma
resolve um problema concreto:

| Decisão | Por quê |
|---|---|
| Senha com `PasswordHasher` do ASP.NET Core (PBKDF2) | Hash lento e salgado, nunca criptografia reversível. |
| Access token JWT de 15 minutos | Janela curta limita o dano de um token capturado. |
| Access token **só em memória** no React, nunca em `localStorage` | Um XSS não rouba a sessão. |
| Refresh token **opaco** em cookie `httpOnly`, `Secure` fora de Development, com `Path` restrito ao endpoint de renovação | O JavaScript não lê o cookie; o cookie não trafega para rotas que não precisam dele. |
| Refresh guardado no banco como **hash**, nunca em texto puro | Vazamento do banco não entrega sessões ativas. |
| **Rotação** do refresh a cada renovação | Reduz a janela de reúso. |
| **Detecção de reúso**: token já usado que reaparece derruba **todas** as sessões do usuário | Reúso é sinal de roubo. A resposta certa é encerrar tudo, não ignorar. |
| `ClockSkew = TimeSpan.Zero` | O padrão de 5 minutos faria um token de 15 viver 20. |
| Resposta **única** `CredencialInvalida` para e-mail malformado, inexistente e senha errada | Não enumera usuários. |
| Hash falso conferido quando o usuário **não existe** | O **tempo de resposta** também não enumera usuários. Defesa contra canal lateral de temporização. |

### Requisitos futuros

- **Rate limiting e bloqueio progressivo** no login, na renovação e na recuperação de
  senha — ver a pendência em 24.19. Requisito de saída da **Fase 10**.
- **Recuperação de conta**, quando existir: token de uso único, curta validade, invalidado
  após o uso, e resposta idêntica para e-mail existente e inexistente.
- **MFA para perfis críticos** (Administrador da Plataforma e da Empresa), a avaliar
  quando houver operação real que justifique. Não implementar por estética.

## 24.4 Autorização e least privilege

- **O backend é a autoridade.** Sempre.
- **O frontend nunca é mecanismo de segurança.** Esconder botão é conforto visual. Um
  botão oculto cuja rota responde 200 é uma falha, não uma proteção.
- **Negar por padrão.** Rota sem política declarada é erro de implementação, não rota
  liberada.
- **Cada endpoint tem política explícita**, nomeada e revisável.
- **Cada perfil recebe só o necessário** — por isso `AdministrarPessoas` é separada de
  `AdministrarEmpresas`, e `ProcessarFolha` é separada das duas: o Analista de RH mantém
  cadastro e calcula folha, mas não administra empresas nem muda o catálogo de rubricas.
- **Operações administrativas exigem privilégio específico**, não "perfil alto".
- **Ações sensíveis poderão exigir autenticação recente** — reautenticar antes de trocar
  senha, alterar permissão ou mudar parâmetro legal. A avaliar quando essas operações
  existirem.

### Matriz de autorização

Manter documentada, e conferida contra o código, uma matriz:

```text
Recurso × Operação × Perfil
```

Perfis: Administrador da Plataforma · Administrador da Empresa · Analista de RH ·
Auditor · Visualizador.

A matriz é **derivada do código**, não o contrário: se documento e código divergirem, o
código é o fato e o documento é o defeito. A auditoria dessa matriz é item do gate da
Fase 12.

## 24.5 Segurança multiempresa

**Nenhum vazamento entre organizações é aceitável.** É o requisito mais crítico do
produto: um erro aqui expõe folha de pagamento de empresa alheia.

### Decisões implementadas — permanentes

- `IdOrganizacao` é derivado **do usuário autenticado**, nunca do corpo, da query string
  ou de header. Enviar `idOrganizacao` numa requisição não tem efeito, e existe teste
  provando.
- **Filtro global** no `PrismaRhDbContext`: toda consulta nasce restrita. Não depende de
  alguém lembrar de escrever `where`.
- **Fail closed**: sem usuário, `Guid.Empty`, que não casa com nada.
- Atravessar a fronteira exige `IgnoreQueryFilters()` **explícito** — visível em revisão.
- Recurso de outra organização devolve **404, não 403**. Um 403 confirmaria que aquele id
  existe e permitiria mapear os dados do vizinho um id por vez.

### Onde o filtro global não alcança

Esta é a parte que exige atenção deliberada, porque o filtro protege consultas dentro de
uma requisição HTTP e **mais nada**:

| Caminho | O que garante o isolamento |
|---|---|
| Consultas e comandos | Filtro global. Já resolvido. |
| **Joins e projeções** | O filtro se aplica à raiz; conferir que navegações e `Include` não escapam. |
| **Relatórios e agregações** | Agregação sobre entidade filtrada; nunca SQL montado à parte. |
| **Exports** | Herdam a autorização do dado exportado, mais registro de quem exportou. |
| **Arquivos** | Chave com prefixo por organização, acesso autorizado, nunca URL pública adivinhável. |
| **Jobs assíncronos** | ⚠️ O job **não tem requisição**. A mensagem carrega `IdOrganizacao` explícito e o worker abre o contexto a partir dela. Conferir contra o objeto processado. |
| **Filas** | Mensagem é dado não confiável: validar esquema e tenant. |
| **Caches futuros** | Chave de cache **inclui a organização**. Cache sem tenant na chave é vazamento com desempenho. |
| **Logs** | Registrar a organização; nunca misturar dado de tenants numa mesma entrada. |
| **Auditoria** | Sempre com organização. |
| **Integrações** | Credencial e destino pertencem ao tenant; o parceiro de A nunca recebe dado de B. |
| **IA** | Uma chamada, um tenant. A consulta gerada roda sob o filtro global — o isolamento não depende do modelo se comportar. |

**Regra obrigatória:** toda funcionalidade nova que manipule dado de tenant entra
acompanhada de **teste de isolamento**. Contra PostgreSQL real, via Testcontainers —
filtro global testado em banco falso não prova nada, porque o EF InMemory não gera SQL.

## 24.6 IDOR / BOLA

*Broken Object Level Authorization* é a falha nº 1 do OWASP API Security, e é a mais
provável neste produto.

**Saber que `/api/funcionarios/{id}` existe não é vulnerabilidade. Devolver o funcionário
de outra organização é.**

Todo endpoint que recebe identificador **confirma o acesso daquele usuário àquele
recurso**. Na prática: resolver o recurso **através** do filtro de organização, nunca por
id direto seguido de conferência manual — a conferência manual é a que se esquece.

Recursos que exigem essa verificação: Empresa, Estabelecimento, Funcionário, Contrato,
Vigência, Folha, Holerite, Lançamento, Rubrica, Inconsistência, Evidência, Arquivo,
Integração e Execução assíncrona.

Recurso aninhado é resolvido **pelo pai**: holerite é encontrado dentro da folha, que está
sob o filtro. Assim um `idHolerite` de outra empresa não encontra caminho.

## 24.7 Validação de entrada

**Toda entrada externa é não confiável** — corpo, query string, header, cookie, nome de
arquivo, conteúdo de arquivo, resposta de parceiro e saída de modelo de IA.

- **validação estrutural** — forma e tipo antes de qualquer uso;
- **validação de domínio** — a entidade protege suas invariantes; CPF e CNPJ validam
  dígito verificador;
- **limites de tamanho** em todo texto;
- **limites de quantidade** em toda coleção;
- **allowlist** onde o conjunto de valores válidos é conhecido; enum fechado, nunca texto
  livre para decisão de negócio;
- **tipos fortes** — nada de `dynamic` ou `object` para contornar modelagem;
- **payload inesperado é rejeitado**, não ignorado em silêncio;
- **paginação com teto** em toda listagem;
- **proteção contra mass assignment e overposting** — requisições usam records de entrada
  próprios, que **não contêm** `Id`, `IdOrganizacao` nem campo calculado. Nunca vincular o
  corpo direto na entidade.

**O backend nunca confia na validação do React.** O frontend valida para dar boa
experiência; o backend valida porque é a autoridade. Toda regra de segurança e de negócio
é repetida no servidor.

## 24.8 Injeção

- **SQL Injection** — o EF Core gera consultas parametrizadas. **Proibido concatenar
  input do usuário em SQL.** `FromSqlRaw`/`ExecuteSqlRaw`, quando inevitáveis, usam
  parâmetros e passam por revisão explícita. Hoje o projeto **não usa SQL bruto em lugar
  nenhum**, e sair disso é decisão consciente, não conveniência.
- **Command Injection** — a aplicação não executa processo do sistema operacional com
  entrada de usuário. Se algum dia precisar, argumentos vão como lista, nunca como linha
  de comando montada por concatenação.
- **Path traversal** — nome de arquivo enviado pelo usuário **nunca** vira caminho. O
  identificador de armazenamento é gerado pelo sistema.
- **Template / expression injection** — nenhuma funcionalidade interpreta expressão
  fornecida pelo usuário. Por isso rubrica tem estratégia por enum e não fórmula em texto,
  e por isso o motor de análises tem parâmetro configurável e não regra escrita pelo
  usuário. Parametrização **nunca** executa código.
- **LDAP injection** — não se aplica hoje. Se um dia houver diretório corporativo, entra
  com allowlist e escape próprios.

## 24.9 XSS

**Stored XSS** é o risco real deste produto, porque a Fase 7 traz comentário,
justificativa e evidência — texto de um usuário lido por outro. Campos expostos: nomes,
descrições, comentários, justificativas, nomes de evidência, dados importados de planilha
e, futuramente, **conteúdo gerado por IA**.

- O React **escapa por padrão**. Texto de usuário é renderizado como texto.
- **`dangerouslySetInnerHTML` é proibido** sem necessidade documentada e revisão
  explícita. Nenhum caso conhecido do produto justifica.
- Não renderizar HTML arbitrário vindo do banco, de import ou de modelo.
- **Reflected XSS**: mensagem de erro nunca ecoa entrada do usuário como HTML.
- **DOM XSS**: nada de `innerHTML`, `eval` ou construção de nó a partir de string de
  usuário.
- Saída de IA é **texto**, exibido como texto. Nunca markup executável.

## 24.10 CSRF

O risco depende do fluxo real, e o fluxo atual já o mitiga — **não adicionar solução
genérica sem analisar**:

- o **access token** vai em header `Authorization`. Não é enviado automaticamente pelo
  navegador, portanto os endpoints que dependem dele **não são vulneráveis a CSRF**;
- o **refresh token** é cookie, e cookie o navegador envia sozinho. Os endpoints
  expostos são `renovar` e `sair`;
- hoje o cookie usa **`SameSite=Lax`**, que **não** acompanha `POST` de outro site. Com
  os dois endpoints sendo `POST`, o CSRF está fechado sem código adicional;
- `HttpOnly` impede leitura por script; `Secure` fora de Development impede trânsito em
  claro; `Path` restrito reduz onde ele trafega;
- **CORS com allowlist explícita** e `AllowCredentials`, com a lista vazia por padrão no
  `appsettings.json` base — falha fechada.

⚠️ **Essa análise muda em produção.** Ver 24.19: com frontend e API em domínios
diferentes, `SameSite=Lax` deixa de enviar o cookie, e a correção reabre o CSRF. A decisão
precede o deploy.

## 24.11 SSRF

Vale a partir da fase de integrações e para **qualquer** funcionalidade que faça
requisição externa, IA inclusa.

> **Implementado em 31/08/2026, na Fase 8.** `GuardaDestino`, em
> `backend/src/PrismaRH.Infraestrutura/Integracoes/`, é a realização desta seção, e passa a
> ser **controle de segurança já implementado** para efeito do §35: enfraquecê-la exige
> decisão registrada do responsável, não conveniência.
>
> O que ela faz, e o que cada item existe para impedir:
>
> | Barreira | Sem ela |
> |---|---|
> | `https` obrigatório, sem userinfo, porta padrão | `https://parceiro@atacante.com` parece a allowlist para quem lê rápido |
> | **Allowlist fixa em código**, de nomes exatos | Em `appsettings`, a única barreira de destino vira um campo que alguém preenche com pressa |
> | DNS resolvido, **todos** os IPs conferidos | Allowlist de *nome* não protege quando o nome passa a apontar para dentro |
> | `::ffff:` desembrulhado antes de decidir | `::ffff:169.254.169.254` é IPv6 "global" que conecta como IPv4 — o desvio clássico |
> | `AllowAutoRedirect = false`, revalidando cada salto, teto de 3 | Validar só a primeira URL não protege: quem escolhe o segundo destino é o parceiro |
>
> A guarda é testada **sem rede**: o resolvedor de DNS é injetado. Defesa de rede testada
> contra a rede real dá uma suíte que falha no avião e passa no escritório.

- **allowlist de destinos** — host e esquema declarados em configuração, nunca digitados
  livremente pelo usuário;
- validar a URL **após resolver o DNS**, bloqueando `localhost`, `127.0.0.0/8`, `::1`,
  faixas privadas (`10/8`, `172.16/12`, `192.168/16`) e **link-local `169.254/16`** —
  esta última cobre o *metadata service* da nuvem, cujo alcance entrega credencial de
  instância;
- **revalidar a cada redirect** e limitar o número deles. Validar só a primeira URL não
  protege;
- **DNS rebinding**: quando relevante, resolver uma vez e conectar ao IP validado;
- **timeout obrigatório** e limite de tamanho de resposta;
- a resposta do parceiro é **dado não confiável**: validada por esquema antes de tocar o
  domínio.

## 24.12 Upload de arquivos

Regras obrigatórias, detalhadas no Security Gate da Fase 5 do `ROADMAP.md`:

tamanho máximo · número máximo de registros · validação de MIME e de conteúdo real ·
**nunca confiar na extensão** · renomear internamente · impedir path traversal · não
executar macro · proteção contra zip bomb e arquivo malformado · limite de memória ·
timeout · armazenamento isolado por organização · acesso autorizado ao arquivo ·
retenção e expiração · malware scanning a avaliar.

**Arquivo nunca é publicamente acessível por padrão.**

Ao **exportar** CSV, prefixar célula que comece com `=`, `+`, `-` ou `@`: sem isso o
arquivo vira fórmula executável na máquina de quem abre (*CSV injection*).

## 24.13 Proteção de dados e classificação

| Classe | O que é | Exemplos no Prisma RH |
|---|---|---|
| **Público** | Pode ser publicado | Nome do produto, documentação de arquitetura |
| **Interno** | Da organização, sem dado pessoal | Catálogo de cargos, catálogo de rubricas, parâmetros de regra |
| **Confidencial** | Dado pessoal identificável | Nome, e-mail, data de nascimento, cargo, lotação |
| **Altamente sensível** | Dano direto se vazar | **CPF**, **salário**, folha e holerite, descontos, benefícios, dependentes, pensão, dados bancários futuros, **credenciais**, **tokens**, justificativas de inconsistência |

Regras por classe:

- **Minimização** — não coletar, não trafegar, não logar, não exportar o que a função não
  exige;
- **Mascaramento** — CPF aparece mascarado na listagem (`111.***.**7-35`) e completo só no
  detalhe. Busca por CPF exige o documento **completo e válido**: busca parcial viraria
  forma de descobrir documentos por tentativa;
- **Exposição só quando necessária** — a API devolve o que a tela precisa, não a entidade
  inteira;
- **Retenção e exclusão** — definir por classe; dado altamente sensível não fica
  indefinidamente em fila, DLQ, log ou arquivo temporário;
- **Backups e exportações** herdam a classificação do conteúdo;
- **Logs sem conteúdo sensível** — ver 24.16.

## 24.14 Criptografia

- **Em trânsito**: produção usa HTTPS/TLS, com HTTP redirecionado e HSTS. Conexão ao
  banco com TLS quando o provedor suportar.
- **Em repouso**: usar a criptografia oferecida pelos provedores (Neon, S3) quando
  aplicável.
- **Senhas**: hashing seguro, lento e salgado. **Nunca** criptografia reversível, nunca
  MD5 ou SHA simples.
- **Tokens**: o refresh é guardado como **hash**, e a forma bruta existe só no cookie do
  usuário — decisão já implementada, a preservar.
- **Proibido criar algoritmo criptográfico próprio.** Usar o que o framework oferece.

## 24.15 Secrets

Secret **nunca** pode: entrar no Git · aparecer no frontend ou no bundle · ser enviado ao
navegador · aparecer em log, nem truncado · entrar em documentação · ser hardcoded.

Ver também §33.

**Estratégia por destino:**

| Onde | Como |
|---|---|
| **GitHub Actions** | GitHub Secrets, com ambientes separados. Pull request de fora do repositório não recebe secret. Preferir **OIDC com papel assumido** a chave de longa duração. |
| **AWS** | Papel IAM para Lambda — **não** chave de acesso. Parameter Store/Secrets Manager se o custo couber no teto. |
| **Vercel** | Variáveis de ambiente do projeto. Atenção: variável com prefixo público **vai para o bundle**. Nenhum segredo com prefixo público. |
| **Neon** | String de conexão via variável de ambiente, nunca em `appsettings` versionado. |
| **Integrações** | Uma credencial por parceiro, rotacionável sem redeploy. |
| **IA** | Chave só no backend. A chamada ao provedor **sai do servidor** — chave de IA no navegador é chave publicada. |

**Rotação.** Todo segredo tem procedimento de rotação escrito antes de entrar em uso, e
rotação é passo obrigatório da resposta a incidente. Em vazamento: **avisar sem mostrar o
valor**, nem truncado, e rotacionar — apagar do arquivo **não** remove do histórico do
Git.

## 24.16 Logging seguro

Log **não** registra: senha · access token · refresh token · cookie · secret · CPF
completo sem necessidade · folha ou holerite completo · dados bancários · payload
sensível integral.

Registrar o suficiente para diagnosticar: identificador da requisição, `correlation id`,
rota, resultado, duração, id da organização e do usuário — **identificadores, não
conteúdo**.

**O log precisa permitir diagnóstico sem virar um banco paralelo de dados pessoais.** Ele
costuma ter retenção diferente, acesso mais amplo e menos proteção que o banco — o que
entra nele sai do regime de proteção do dado original.

## 24.17 Auditoria de negócio

**Log técnico não substitui auditoria de negócio** (§26). O técnico é rotativo e
descartável; a auditoria é registro do produto.

Todo evento sensível registra: **usuário · organização · ação · entidade · identificador ·
data · resultado · contexto relevante**.

Eventos a auditar: alteração salarial · alteração contratual · criação, reprocessamento e
fechamento de folha · alteração de rubrica · **alteração de parâmetro legal** ·
tratamento de inconsistência · administração de usuários · alteração de permissão ·
configuração de integração · participação de sugestão de IA numa decisão.

**Registro de auditoria não é alterável por usuário comum** — de nenhum perfil. Não
existe endpoint de edição nem de exclusão de auditoria. É tabela somente-inserção.

## 24.18 Rate limiting, abuso e negação de serviço

### Rate limiting

Alvos: login · renovação · recuperação de senha · endpoints anônimos · uploads · chamadas
a API externa · **IA** · relatórios caros · buscas.

Dimensões a combinar: **IP · usuário · organização · endpoint**. Só por IP não protege
contra credential stuffing distribuído; só por usuário não protege contra tentativa
espalhada por muitos e-mails.

**Nenhuma organização pode causar custo ou indisponibilidade para outra.** Num sistema
multiempresa com recursos compartilhados, abuso de um tenant é problema de todos.

### Negação de serviço e exaustão de recursos

- **paginação obrigatória**, com página máxima;
- limite de filtros e de complexidade de busca;
- limite de upload, de registros e de profundidade;
- **timeout** em toda operação de I/O;
- **`CancellationToken`** propagado — requisição abandonada pelo usuário não continua
  consumindo banco;
- limite de concorrência;
- trabalho pesado vira job (Fase 9);
- endpoints caros protegidos por limite específico;
- **consulta sem limite é proibida** onde houver volume relevante.

## 24.19 ⚠️ Pendências de segurança conhecidas

Registradas em **27/08/2026**, após auditoria do código existente. Ambas são aceitáveis
enquanto o sistema roda só em `localhost`, e **bloqueantes antes do primeiro deploy
público**.

> **Atualizado em 30/08/2026.** Os itens **6** e **7** foram **resolvidos na Fase 7**, pela
> trilha de auditoria somente-inserção. Ficam registrados aqui, com a correção anotada no
> próprio item, porque apagar uma pendência resolvida apaga também a prova de que ela foi
> encontrada e tratada. Continuam abertos os itens **1 a 5**.

### 1. ✅ RESOLVIDA — Não existe rate limiting em nenhum endpoint

O login já é constante no tempo e não enumera usuários — as duas defesas difíceis estão
feitas. Mas nada impede milhares de tentativas por minuto contra
`POST /api/autenticacao/entrar`. Força bruta e credential stuffing estão abertos.

**Resolver na Fase 10**, como requisito de saída. Não esperar a Fase 12.

> **Resolvida em 01/09/2026, na Fase 10.** Limite **por IP** — 10/min em
> `entrar`, 60/min em `renovar` e `sair`.
>
> Por IP, e não por e-mail: no login **não há usuário ainda**, e é isso que o
> atacante está tentando descobrir. Particionar por e-mail deixaria um script
> varrer mil endereços sem estourar limite nenhum, que é a forma do *credential
> stuffing*.
>
> ⚠️ Continua **pendente** o bloqueio progressivo **por conta**, que o §24.18
> pede ao lado do limite por IP. Ele entra na Fase 12.

### 2. ✅ RESOLVIDA — `SameSite=Lax` não sobrevive à topologia de produção

Em produção o frontend fica na Vercel e a API no API Gateway — **domínios registráveis
diferentes, portanto cross-site**. Com `SameSite=Lax` o navegador **não envia o cookie**,
e a sessão não sobrevive a um recarregamento.

O problema não é só funcional: a correção óbvia é `SameSite=None; Secure`, e **isso
reabre o CSRF que o `Lax` fechava de graça**. Trocar por reflexo na pressa do deploy
substituiria uma falha visível por uma silenciosa.

Duas saídas, **decisão do responsável antes de publicar**: (a) servir API e frontend sob o
mesmo domínio registrável, mantendo `Lax`; ou (b) `SameSite=None; Secure` com defesa CSRF
explícita — *double submit cookie* ou token anti-CSRF, mais validação de `Origin` na
renovação. Detalhes no Security Gate da Fase 10 do `ROADMAP.md`.

> **Resolvida em 01/09/2026, na Fase 10.** Saída **(b)**, escolhida pelo
> responsável: `SameSite=None; Secure` com defesa CSRF explícita.
>
> `GuardaCsrf` (em `backend/src/PrismaRH.Api/Producao/`) tem **duas** barreiras,
> e passa a ser controle de segurança implementado para efeito do §35:
>
> | Barreira | O que ela impede |
> |---|---|
> | **Double submit cookie** — segundo cookie legível por JS, repetido no cabeçalho `X-CSRF-Token`, comparado em **tempo constante** | O site atacante consegue fazer o navegador **enviar** o cookie; a same-origin policy o impede de **ler** o valor |
> | **Validação de `Origin`**, com allowlist de **hostname exato** | `Origin` é preenchido pelo navegador e não é forjável por script de página. **Ausência é recusa**, não exceção |
>
> Em Development o cookie continua `Lax` e a guarda não é exigida — `None` sem
> HTTPS é descartado pelo navegador.

### 3. ✅ RESOLVIDA — Listagens sem paginação

`GET /api/folhas`, `/api/rubricas`, `/api/cargos`, os estabelecimentos, os holerites e os
lançamentos devolvem tudo. Empresas e funcionários já têm teto de 100 por página.

**Resolver na Fase 10.** Sem impacto em `localhost`; com volume real, é vetor de exaustão.

> **Resolvida em 01/09/2026, na Fase 10.** Duas defesas, para dois casos:
> **envelope paginado** (`{total, paginaAtual, tamanho, itens}`, padrão 50, teto
> 200) nas listagens que crescem sem limite — folhas, rubricas, cargos; e **teto
> rígido de 500** nos sub-recursos limitados por natureza, onde paginar seria
> cerimônia e mudar o contrato quebraria a tela sem ganho.
>
> A ordenação é **determinística** em todas: `ORDER BY` que termina em campo
> único. Sem isso, `OFFSET`/`LIMIT` no PostgreSQL pode repetir ou pular linha
> entre páginas — o planejador não promete ordem.

### 4. ✅ RESOLVIDA — Entrada malformada devolve 500 em vez de 400

Registrada em **27/08/2026**, durante a Fase 4D.

Um enum com valor desconhecido no corpo da requisição — `"relacao": "Papagaio"` —
devolve **500 Internal Server Error**. O mesmo vale para JSON malformado. Não é defeito de
uma rota: foi conferido contra `POST /api/contratos/{id}/vigencias`, da Fase 2, e a API
inteira se comporta assim. A causa é o `UseExceptionHandler` tratando a falha de binding
como erro do servidor.

**Não há vazamento nem furo de autorização**: o valor inválido é rejeitado e nunca chega
ao domínio, e o `ProblemDetails` não expõe stack trace. O problema é de contrato e de
diagnóstico — o cliente não consegue distinguir "eu mandei errado" de "o servidor caiu", e
um 500 recorrente mascara falha real no monitoramento.

A correção mexe no tratamento de erro de **todas** as rotas, e por isso não foi feita
dentro da subfase que a encontrou. **Resolver na Fase 10**, junto com os demais itens de
robustez da API, mapeando `BadHttpRequestException` para 400.

> **Resolvida em 01/09/2026, na Fase 10.** `TratamentoDeErro` mapeia falha de
> **protocolo** para 400, e corpo acima do teto para 413.
>
> Precisa ser um `IExceptionHandler` no pipeline, e nao `try/catch` na rota: a
> falha acontece no **binding do corpo**, antes de o codigo da rota comecar.
>
> ⚠️ A resposta **nao** devolve o detalhe do parser. A mensagem do
> `System.Text.Json` costuma incluir um trecho do JSON — que e entrada nao
> confiavel e pode conter dado pessoal. Ha teste exigindo a ausencia.

### 5. ✅ ENCERRADA — IRRF de férias apurado em separado (era premissa errada)

Registrada em **28/08/2026**, durante a Fase 4E.

A Fase 4E trouxe a **folha de férias**, e com ela a primeira situação em que a mesma
pessoa recebe por **duas folhas na mesma competência**. O IRRF de cada uma é apurado sobre
ela mesma.

Como a tabela é **progressiva**, dois rendimentos separados caem em faixas mais baixas do
que a soma cairia: o imposto retido fica **menor do que o devido**. Um salário de R$ 4.000
mais R$ 4.000 de férias não é tributado como R$ 8.000.

**Não é falha de segurança nem de isolamento — é correção fiscal**, e o efeito é a favor
do contribuinte na retenção e contra ele no ajuste anual.

Resolver exige decidir em qual folha o imposto é retido, o que fazer quando a mensal é
calculada **depois** da de férias, e como reprocessar.

> **Correção registrada em 29/08/2026, ao concluir a Fase 4F.**
>
> O texto original dizia que a Fase 4F traria "a mesma classe de problema" no 13º. **Não
> traz, e a diferença é de direito.**
>
> O 13º tem **tributação exclusiva na fonte**, apurada em separado dos demais rendimentos
> do mês por determinação legal. O MOS eSocial S-1.3, item 10.3.4, é explícito: *"no mês de
> dezembro são geradas duas folhas pelo eSocial: dezembro e 13º salário (...) o contribuinte
> deve transmiti-las de forma independente"*.
>
> Ou seja: **no 13º, apurar em separado é o comportamento correto**; nas férias é defeito.
> Esta pendência alcança **férias e mensal do mesmo mês**, e nada mais.

> ### ⚠️ NÃO ERA DEFEITO. Pendência encerrada em 01/09/2026, na revisão pós-roadmap.
>
> **A premissa acima está errada, e a fonte oficial diz o contrário.** O comportamento do
> sistema — apurar o IRRF de férias em separado da mensal — **é o exigido por norma**.
>
> **IN RFB 1.500/2014, art. 29** (§ 5º incluído pela IN RFB 2.141/2023; artigo não
> revogado):
>
> > *"Art. 29. No caso de pagamento de férias, inclusive as pagas em dobro (...) a base de
> > cálculo corresponde ao salário relativo ao mês de férias, acrescido, conforme o caso,
> > de 1/3 (um terço) do seu valor.*
> >
> > *§ 1º O cálculo do imposto deve ser efetuado **em separado de qualquer outro rendimento
> > pago no mês**, inclusive no caso de férias indenizadas, ainda que proporcionais, pagas
> > em rescisão de contrato de trabalho.*
> >
> > *§ 3º Na determinação da base de cálculo podem ser efetuadas as deduções previstas no
> > art. 52, desde que correspondentes às férias.*
> >
> > *§ 4º Na DAA, as férias devem ser tributadas em conjunto com os demais rendimentos."*
>
> O **MAFON da Receita Federal**, seção FÉRIAS, repete: *"deve ser tributado no mês de seu
> pagamento e em separado de qualquer outro rendimento pago no mês"*.
>
> **De onde veio o engano.** A regra geral que a pendência invocou **existe**, e está no
> mesmo manual, no código 0561: *"se, no mês, houver mais de um pagamento, a qualquer
> título, pela mesma fonte pagadora, aplicar-se-á a alíquota correspondente à soma dos
> rendimentos pagos"*. A pendência aplicou essa regra às férias sem saber que o art. 29 é
> **norma especial**, e norma especial afasta a geral no caso que ela regula.
>
> **Não há conflito entre fontes** — há especialidade. Por isso a revisão não parou aqui.
>
> **E o "prejuízo" apontado?** Existe, é conhecido, e é o **desenho legal**: apurar em
> separado retém menos do que a soma reteria, e o § 4º manda somar tudo na **declaração
> anual**. Retenção é antecipação, não imposto final.
>
> **O que mudou no código:** nada de cálculo. O que entrou foi
> `backend/testes/PrismaRH.Testes/Dominio/IrrfFeriasEmSeparadoTestes.cs`, que **trava** o
> comportamento correto — para ninguém "corrigir" isto no futuro somando as duas folhas,
> que erraria contra o contribuinte.
>
> **Dois achados colaterais**, ambos vindos de testes meus que falharam:
>
> 1. **Abaixo de ~R$ 5.000 a questão é irrelevante**: o redutor da Lei 15.270/2025 zera o
>    imposto, e separar ou somar dá zero nos dois casos.
> 2. **A dedução por dependente só muda o resultado quando as deduções legais superam o
>    desconto simplificado.** Sem INSS na conta, o simplificado (R$ 607,20) vence dois
>    dependentes (R$ 379,18) e a `CalculadoraIrrf` escolhe o simplificado — que é o art. 29
>    § 5º, *"caso seja mais benéfico ao contribuinte"*.
>
> ⚠️ **Ponto declarado, não resolvido:** se a dedução por dependente pode ser usada **nas
> duas** apurações do mesmo mês. O texto oficial que consegui diz apenas *"correspondentes
> às férias"*; fontes secundárias especializadas afirmam que sim, *"sem prejuízo dessa
> mesma dedução (...) sobre os salários pagos no mesmo mês"*, mas não localizei essa frase
> em fonte primária. O sistema segue as fontes disponíveis e **não** foi alterado —
> mudá-lo seria decidir regra fiscal por interpretação, que o `§29` proíbe.

### 6. ✅ RESOLVIDA — Valor base do FGTS rescisório: sem autor e sem histórico

Registrada em **29/08/2026**, durante a Fase 4G, etapa 3.

O **Valor Base do FGTS para Fins Rescisórios** é entrada humana que **multiplica
dinheiro**: 40% ou 20% dele viram a indenização compensatória. A entidade
`ValorBaseFgtsRescisorio` guarda o valor, uma observação e `InformadoEm` — mas **não guarda
quem informou**, e corrigir o valor **sobrescreve** o anterior sem deixar histórico.

O `CLAUDE.md §24.17` manda auditar exatamente esta classe de evento, e §24.17 também diz
que registro de auditoria não é alterável. Aqui o dado é alterável por design — corrigir
uma medida é legítimo —, mas então a **alteração** é que precisa ficar registrada.

**Aceitável enquanto o sistema roda só em `localhost` e a folha de rescisão ainda não é
fechada.** Resolver na **Fase 7**, junto com a trilha de auditoria de negócio, que é onde o
fechamento de folha e a alteração de parâmetro legal também entram. Registrar autor, valor
anterior, valor novo e data — a mesma tabela somente-inserção dos demais eventos.

Até lá, a rastreabilidade disponível é a **memória de cálculo** da verba `MULTAFGTS`, que
mostra qual número foi usado.

**Bloqueante antes de qualquer uso real.** Registrada também no `README.md` e no bloco da
Fase 4E do `ROADMAP.md`.

> **Resolvida em 30/08/2026, na Fase 7.**
>
> `PUT /api/contratos/{id}/rescisao/valor-base-fgts` passou a **ler o valor anterior antes de
> sobrescrever** e a registrar um `EventoAuditoria` na mesma transação da alteração: quem
> informou, valor anterior, valor novo, data e o contrato. O evento é `ValorBaseFgtsInformado`
> quando é a primeira medida e `ValorBaseFgtsCorrigido` quando há um valor sendo substituído —
> a distinção existe porque corrigir um número que já multiplicou dinheiro é o evento que
> importa auditar.
>
> A tabela `eventos_auditoria` é **somente-inserção**: não há método de domínio, nem endpoint,
> nem perfil que altere ou apague uma linha. Consultável em `/auditoria`, filtrando por
> `ValorBaseFgtsRescisorio`.
>
> ⚠️ O `ValorBaseFgtsRescisorio` continua guardando **só o valor vigente** — quem quer a série
> histórica lê a trilha, e não a entidade. Foi decisão: duplicar a série na entidade criaria
> uma segunda fonte de verdade que pode divergir da trilha.

### 7. ✅ RESOLVIDA — Alteração de regra de análise guarda só a última, sem histórico

Registrada em **30/08/2026**, durante a Fase 6.

`RegraAnalise` guarda `AlteradoPor` e `AlteradoEm` — quem mexeu **por último**, e quando.
Não guarda o histórico: quem afrouxou a tolerância de 70% para 95% na semana passada, e
qual era o valor antes.

O §24.17 manda auditar **alteração de parâmetro de regra**, e a razão é direta: afrouxar
uma tolerância é o jeito mais barato de fazer uma divergência sumir do relatório de
conferência. Sem histórico, a mudança é indistinguível de "sempre foi assim".

**Aceitável enquanto o sistema roda só em `localhost` e ninguém depende do relatório.**
Resolver na **Fase 7**, na mesma tabela somente-inserção do `ValorBaseFgtsRescisorio`
(item 6) e do fechamento de folha — autor, valor anterior, valor novo, data.

Até lá, a rastreabilidade disponível é a **severidade e a versão congeladas em cada
resultado**: um achado de agosto continua dizendo com qual régua foi produzido, mesmo
depois de a régua mudar.

**Bloqueante antes de qualquer uso real.** Registrada também no bloco da Fase 6 do
`ROADMAP.md`.

> **Resolvida em 30/08/2026, na Fase 7.**
>
> Configurar uma regra passou a gravar um `EventoAuditoria` `RegraAnaliseConfigurada` na mesma
> transação: quem alterou, quando, e o **contexto completo** — código da regra, se ficou ativa,
> a severidade escolhida e **cada parâmetro com o valor que recebeu**. Afrouxar a tolerância de
> 70% para 95% agora deixa rastro em `/auditoria`, filtrando por `RegraAnalise`.
>
> O contexto é escrito em cultura **invariante** (`percentualTolerancia=95`, ponto decimal),
> porque é campo técnico comparável entre registros; a descrição legível ao lado usa pt-BR. As
> duas formas convivem de propósito — uma para a pessoa, outra para o diff.
>
> Continua valendo a rastreabilidade que já existia e é complementar: cada `ResultadoAnalise`
> congela a **versão da regra e a severidade** do momento em que foi produzido, então um achado
> de agosto segue dizendo com qual régua saiu, mesmo depois de a régua mudar (`§4.3`).

### 8. Chave de acesso AWS exposta, e mantida em uso por decisão do responsável

Registrada em **31/08/2026**, ao configurar o acesso AWS da Fase 9.

O par de chaves do usuário IAM `portfolio-cli-bootstrap` — que tem **`AdministratorAccess`**
na conta `632404567709` — foi transmitido em texto puro por um canal que fica gravado em
disco. O valor não é reproduzido aqui, nem truncado, pelo `§24.15`.

A correção padrão é **rotação**: criar o novo par, trocar quem consome, confirmar, apagar o
antigo. Apagar a mensagem não resolve — o segredo já foi escrito em arquivo, pela mesma
razão que apagar do código não o tira do histórico do Git.

> **O responsável foi avisado em 31/08/2026 e decidiu não rotacionar.** É conta pessoal de
> portfólio, sem dado de terceiro, com orçamento mensal de US$ 31 e MFA na raiz. A decisão
> está registrada aqui, e não repetida a cada tarefa.

**O que isso muda na prática, e é por isso que fica escrito:**

- a chave abre a conta inteira, não só o Prisma RH — o raio de dano de um vazamento é toda
  a infraestrutura do portfólio, não um bucket;
- enquanto ela viver, **o alerta de orçamento deixa de ser conforto e vira detecção**: é o
  que avisa se alguém começar a gastar na conta;
- **nada muda para a aplicação.** A Lambda da Fase 9 usa **papel IAM**, nunca chave de
  longa duração (`§24.15` e o Security Gate da Fase 9, item 9). Esta chave é do
  desenvolvimento, e não do produto.

**Bloqueante antes de qualquer uso que não seja portfólio pessoal.**

### 9. ✅ RESOLVIDA — Segredos de produção em variável de ambiente de Lambda

Registrada em **01/09/2026**, na auditoria pós-roadmap.

As duas funções Lambda guardam segredos em **variáveis de ambiente**, e a AWS as devolve em
**texto puro** para qualquer chamada de `lambda:ListFunctions` ou
`lambda:GetFunctionConfiguration`:

| Função | Segredo exposto |
|---|---|
| `portfolio-prisma-rh-prod-api` | `PRISMARH_NEON_CONNECTION` (senha do banco) e **`Jwt__ChaveAssinatura`** |
| `portfolio-prisma-rh-worker-importacao` | `PRISMARH_NEON_CONNECTION` |

Os valores **não são reproduzidos aqui**, nem truncados (`§24.15`).

**Isto não é uma configuração errada** — é o comportamento documentado da AWS. O problema é
o que ele significa combinado com o resto:

> **A cadeia completa.** O `item 8` registra que existe uma chave de acesso IAM com
> `AdministratorAccess` que foi exposta e que o responsável decidiu **não rotacionar**.
> Quem tiver aquela chave lê estas variáveis com um comando. E lendo-as obtém a senha do
> banco de produção **e a chave de assinatura do JWT**.

⚠️ **A chave do JWT é a pior das duas.** Com ela um atacante **forja token para qualquer
usuário de qualquer organização** — o que derruba de uma vez o filtro global, a matriz de
perfis e o isolamento multiempresa inteiro. Nenhuma das defesas construídas nas Fases 1 a
12 resiste a um token legitimamente assinado.

⚠️ **A senha do banco apareceu na saída de um comando durante esta auditoria**, e ficou
gravada no histórico da sessão. Isso soma ao caso do `item 8`: a recomendação é
**rotacionar**, não apagar o registro — apagar não remove de onde já foi escrito.

**O que se recomenda, em ordem:**

1. **Rotacionar a `Jwt__ChaveAssinatura`.** É a de maior alcance, e a rotação é barata: o
   efeito colateral é derrubar as sessões ativas, o que num portfólio não custa nada.
2. **Rotacionar a senha do Neon** e atualizar as duas funções.
3. **Mover os segredos para o SSM Parameter Store (`SecureString`)**, lidos no *cold start*.
   Parâmetro padrão não tem custo mensal, e o `SecureString` usa a chave gerenciada pela
   AWS, que também não tem — as chamadas de `Decrypt` caem na franquia do KMS. ⚠️ O preço
   vigente precisa ser confirmado antes de criar qualquer coisa (`§16`), e criar recurso
   AWS exige autorização explícita.

**Por que não foi corrigido nesta revisão:** mover segredo de lugar é alteração de
infraestrutura e exige **deploy**, que o `§31` condiciona a autorização explícita na
mensagem. A revisão pós-roadmap não a tinha.

**Bloqueante antes de qualquer uso que não seja portfólio pessoal.**

> ### ✅ RESOLVIDA em 02/09/2026, na correção final de segurança pós-roadmap.
>
> **A estratégia, em uma linha:** a Lambda carrega o **nome** do parâmetro; o segredo vive
> no **SSM Parameter Store** como `SecureString`; a criptografia usa a **chave gerenciada
> pela AWS**, sem nenhuma CMK paga.
>
> ```text
> variavel de ambiente da Lambda  ->  /portfolio/prisma-rh/prod/database
>                                     /portfolio/prisma-rh/prod/jwt-signing-key
> SSM SecureString                ->  o segredo
> alias/aws/ssm                   ->  criptografia sem custo fixo
> ```
>
> Nome de parâmetro não é segredo. Ler o valor passa a exigir `ssm:GetParameter(s)`
> **naquele ARN específico** mais `kms:Decrypt` — e o `Decrypt` está restrito por
> `kms:ViaService = ssm.us-east-1.amazonaws.com`, então a permissão só vale **através do
> SSM**, nunca direto contra a chave.
>
> **Privilégio mínimo, papel a papel:** a API alcança os dois parâmetros; o worker alcança
> **só o do banco** — ele não precisa da chave do JWT, e por isso não a recebe.
>
> **Custo: US$ 0,00 fixos**, verificado na documentação vigente **antes** de criar
> qualquer coisa (`§16`): *"Standard parameters are available at no additional charge"*, e
> para a chave gerenciada *"You are not charged for (...) creation and storage of AWS
> managed (...) KMS keys"*, com franquia de **20.000 requisições/mês** de KMS.
>
> ⚠️ A franquia só é folgada porque a busca é **uma por container, no startup** — não por
> requisição. Buscar por requisição transformaria um portfólio ocioso numa conta.
>
> **Segredos rotacionados:**
>
> | Segredo | Situação |
> |---|---|
> | `Jwt__ChaveAssinatura` | ✅ **Rotacionada.** Valor novo gerado por CSPRNG, escrito direto no cofre, nunca exibido. Sessões antigas caíram — efeito esperado. |
> | Access key IAM `portfolio-cli-bootstrap` | ✅ **Rotacionada.** Nova criada, profile local atualizado, identidade verificada, antiga desativada, testada de novo e só então **excluída**. `AdministratorAccess` preservado. |
> | Conexão do Neon | ✅ **Movida para o cofre e rotacionada em 02/09/2026** pelo responsável, no console do Neon. Ver abaixo. |
>
> ✅ **A senha do Neon foi rotacionada em 02/09/2026** pelo responsável, no console do
> Neon, e o parâmetro `/portfolio/prisma-rh/prod/database` foi sobrescrito (versão 2).
>
> Provado em produção depois de reciclar os containers: a API responde `/health`
> `saudavel` com o *check* de banco, e o worker registra
> `[prova-neon] trabalho ... nao existe` — mensagem que **só existe depois de consultar
> o banco**. Nenhuma Lambda precisou ser republicada: elas leem o cofre no cold start.
>
> **Verificação executada.** O comando que expunha os segredos foi rodado de novo:
>
> ```text
> aws lambda get-function-configuration --function-name portfolio-prisma-rh-prod-api
>   -> ASPNETCORE_ENVIRONMENT, Cors__OrigensPermitidas__0,
>      DOTNET_SYSTEM_GLOBALIZATION_INVARIANT, PRISMARH_SQS_URL,
>      PRISMARH_SSM_PARAMETRO_BANCO, PRISMARH_SSM_PARAMETRO_JWT
>   -> nenhuma senha, nenhuma connection string, nenhuma chave
> ```
>
> E a prova de que a rotação **propagou**: um token assinado com a chave lida do cofre foi
> aceito pela API (`HTTP 200`), devolvendo página vazia — porque a organização do token é
> inventada e o filtro global não casa com nada. Autenticação funciona, e o isolamento
> resiste até a um token legitimamente assinado.
>
> ⚠️ **Um defeito meu, cometido e corrigido no meio desta tarefa, fica registrado.** A
> primeira versão injetava a chave via `AddOptions<OpcoesJwt>().Configure(...)`, e a API
> **caiu em produção** com `IDX10703: key length is zero`. A causa: há **dois caminhos
> independentes** lendo o mesmo segredo — o `GeradorJwt` **emite** token pelo `IOptions`, e
> o `AddJwtBearer` do `Program.cs` **valida** lendo a configuração direto. A correção
> cobria só o primeiro. O serviço foi restaurado em seguida repondo as variáveis, e a
> correção definitiva alimenta a **configuração**, que é de onde os dois nascem.
>
> ⚠️ **Um resto que eu criei e apaguei:** um parâmetro `SecureString` com a conexão de
> produção foi criado por engano em **sa-east-1**, porque `AWS_DEFAULT_REGION=sa-east-1`
> está no ambiente desta máquina e o AWS CLI v1 **não lê `AWS_REGION`**. Localizado numa
> varredura por região e **excluído**. Só `us-east-1` tem parâmetros.

### 10. ✅ RESOLVIDA — Guarda CSRF impossível de satisfazer em produção

Registrada e resolvida em **02/09/2026**, no endurecimento da demonstração pública.

O frontend lia o token do *double submit* com `document.cookie`. Isso funcionava em
desenvolvimento, onde tela e API vivem em `localhost`, e **nunca funcionou publicado**:

```text
tela  ->  portfolio-prisma-rh.vercel.app
API   ->  ...lambda-url.us-east-1.on.aws     <- o cookie mora AQUI
```

`document.cookie` é **por origem**. A página da Vercel jamais enxergou o cookie gravado
pelo domínio da Lambda, então o cabeçalho `X-CSRF-Token` nunca era enviado e `renovar` e
`sair` respondiam **403** — o 403 que aparecia no console de toda visita.

⚠️ **A guarda estava correta; a tela é que não conseguia satisfazê-la.** O efeito visível
era pior que o erro no console: **um F5 deslogava**, porque o access token vive só em
memória e a renovação era o único caminho de volta.

> **Correção:** o token passa a vir **no corpo** de `entrar` e de `renovar`, e a tela o
> guarda no `sessionStorage` da própria origem.
>
> Nada afrouxou. O que protege o *double submit* não é o cookie ser legível — é o site
> atacante **não descobrir o valor**, e ele continua sem: não lê a origem da tela, não lê
> o cookie e não lê a resposta, porque o CORS tem allowlist de origem. O servidor segue
> exigindo cookie **e** cabeçalho iguais, mais `Origin`, com ausência = recusa.
>
> ⚠️ E o cookie ficou **mais** restrito: virou `HttpOnly`, já que o script não precisa
> mais lê-lo.

**Por que a suíte não pegou:** os testes de front rodam em `jsdom`, que é *same-origin* —
o cookie escrito pelo teste era lido pelo mesmo código. O ambiente de teste não
reproduzia a topologia da produção.

### 11. ✅ RESOLVIDA — Motor de cálculo quebrado em produção por dependência de ICU

Registrada e resolvida em **02/09/2026**, usando a produção como usuário.

`POST /api/folhas/{id}/calcular` devolvia **500** desde o primeiro deploy:

```text
System.TypeInitializationException: The type initializer for
  'PrismaRH.Dominio.Folha.MotorCalculoFolha' threw an exception.
---> CultureNotFoundException: Only the invariant culture is supported in
  globalization-invariant mode.
```

Nove classes tinham `static readonly CultureInfo Brasil =
CultureInfo.GetCultureInfo("pt-BR")`. A Lambda roda com
`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`, porque o runtime `provided.al2023` não traz
ICU — e nesse modo pedir cultura por nome **lança**. Por ser inicializador **estático**, a
falha derrubava a classe inteira antes da primeira conta.

⚠️ **1258 testes verdes não pegaram**, porque a máquina de desenvolvimento e o runner do
CI têm ICU. O mesmo código passa nos dois e falha só onde importa.

Um segundo defeito da mesma família apareceu junto: a busca de coluna do CSV usava
`Normalize(FormD)` para ignorar acento. Em modo invariante isso **não lança** — devolve a
string intacta. Um cabeçalho `Salário` simplesmente não era encontrado, e a importação
aceitava o arquivo com a coluna faltando. **Falha silenciosa em importação de folha é pior
que erro.**

> **Correção em três camadas:**
>
> | Camada | O quê |
> |---|---|
> | `FormatoBrasileiro` | `NumberFormatInfo` montado à mão, sem ICU, sem pacote maior |
> | `ResultadoLeitura` | tabela explícita de letras acentuadas do português, determinística em qualquer máquina |
> | **CI** | a suíte roda **duas vezes**, a segunda com `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` |
>
> A terceira é a que impede a classe de defeito de voltar. As duas primeiras corrigem o
> que existe; só a terceira pega o próximo.

**A lição, e ela é geral:** *ambiente de teste verde não prova ambiente de produção.* É a
mesma família do modelo de IA aposentado (`§37.8`) — em ambos, a suíte passava e a
realidade não.

### 12. ✅ RESOLVIDA — Senha compartilhada entre os seis usuários da semeadura

Registrada e resolvida em **02/09/2026**, ao publicar a conta pública da demonstração.

`SemeadorDesenvolvimento` gerava **um** hash e o atribuía aos seis usuários. Conferido em
produção antes de qualquer mudança:

```sql
SELECT count(DISTINCT senha_hash), count(*) FROM usuarios;  -- 1 | 6
```

⚠️ **Publicar a senha do Visualizador publicaria a do Administrador da Plataforma**, e os
endereços seguem um padrão óbvio, visível no próprio arquivo de semeadura. Uma senha
compartilhada vale o **menor** privilégio de quem a usa, não o maior.

> **Correção:** a conta pública ganhou senha própria, por `PRISMARH_SEED_SENHA_DEMO`, no
> código e em produção. Sem essa variável o Visualizador nasce com a senha comum — que é o
> certo em desenvolvimento, onde nada é publicado.
>
> A prova que autoriza publicar a credencial é
> `testes/Isolamento/VisualizadorSomenteLeituraTestes.cs`: ele **descobre as rotas de
> escrita em tempo de execução** pelo `EndpointDataSource` e exige 403 em todas. Rota de
> escrita criada amanhã entra no teste sozinha.

## 24.20 Headers, CORS e navegador

**Headers** a planejar e validar contra o frontend real na Fase 10:
`Content-Security-Policy` · `HSTS` · `X-Content-Type-Options: nosniff` ·
`Referrer-Policy` · `Permissions-Policy` · `frame-ancestors` contra clickjacking.

**Não inserir configuração aleatória.** Uma CSP copiada de exemplo quebra o frontend ou
vira `unsafe-inline`, que não protege. A política é construída a partir do que a
aplicação realmente carrega.

**CORS**: produção usa **allowlist explícita**. **Jamais `*` com credenciais** — a
combinação é rejeitada pelos navegadores e, se contornada, anula a proteção de origem.
Development e Production têm configuração separada, e o `appsettings.json` base traz lista
**vazia**, para falhar fechado.

## 24.21 Banco de dados

- usuário da aplicação com **privilégio mínimo**; a aplicação **não usa superuser**;
- migration aplicada por credencial distinta da credencial de execução, quando o ambiente
  permitir;
- **constraints no banco** — a garantia final não é o C#. O histórico contratual é
  protegido por *constraint* de exclusão que impede sobreposição de períodos mesmo sob
  requisições simultâneas;
- transações onde a operação exigir atomicidade;
- backups do provedor, com **restore testado**;
- string de conexão só por variável de ambiente;
- **TLS** na conexão em produção quando suportado;
- acesso administrativo restrito; banco **não** exposto publicamente sem necessidade.

## 24.22 Infraestrutura AWS

Princípios, a aplicar quando as fases correspondentes chegarem — **nada disso é
implementado antes**:

least privilege no IAM · **nenhuma chave root** · MFA na conta · root fora do dia a dia ·
recursos **privados por padrão** · **S3 Block Public Access** · IAM **por função**, não
por pessoa · políticas mínimas · CloudWatch com retenção definida · budgets com alerta ·
tags de custo · **sem NAT Gateway sem autorização** · nenhum banco publicamente exposto
sem necessidade.

Cada recurso é avaliado **antes** de ser criado (§16).

## 24.23 Backup e recuperação

Disponibilidade é parte de segurança.

Planejar backup, restauração, **RPO** e **RTO** conceituais, teste de restore, recuperação
após migration problemática e recuperação de dado removido por acidente.

**Estratégia proporcional ao custo** — não contratar serviço caro só para cumprir
checklist. E **backup nunca testado é hipótese, não garantia**.

## 24.24 Resposta a incidente

```text
Detectar → Conter → Investigar → Corrigir → Rotacionar credenciais →
Restaurar → Documentar → Prevenir recorrência
```

Conter vem antes de investigar: parar o sangramento primeiro. Rotação é passo próprio
porque é o mais esquecido. Detalhes e a tabela por tipo de incidente estão na Fase 12 do
`ROADMAP.md`.

## 24.25 Dependências e supply chain

Versões controladas · lockfiles versionados · análise de vulnerabilidade em NuGet e npm ·
Dependabot ou equivalente · revisão antes de major upgrade · GitHub Actions fixadas por
versão · nenhuma action de origem desconhecida.

**Evitar pacote abandonado. Evitar pacote desnecessário. Não instalar biblioteca para
funcionalidade trivial** — cada dependência é superfície de ataque, e a maior parte dos
incidentes de supply chain entra por pacote pequeno que ninguém revisa.

## 24.26 Testes de segurança

São **parte da implementação**, não etapa de auditoria. Categorias e detalhe na Fase 12 do
`ROADMAP.md`: autenticação · autorização · **isolamento multiempresa** · IDOR · validação
· uploads · limites · endpoints sensíveis · vulnerabilidade de dependências · SAST/DAST
quando fizer sentido.

Testes de intrusão **somente em ambientes sob controle do projeto**. Nunca contra serviço
de terceiro.

## 24.27 Threat modeling

Antes de funcionalidade que crie superfície relevante nova — autenticação, upload,
integração externa, processamento AWS, IA, endpoint anônimo — fazer um threat model curto:

```text
Ativo | Ameaça | Vetor | Impacto | Controle | Teste | Risco residual
```

Uma tabela, não um documento. Threat modeling que vira burocracia deixa de ser feito.

## 24.28 Padrões de referência

**OWASP Top 10** · **OWASP ASVS** · **OWASP API Security Top 10** · práticas de segurança
da Microsoft para .NET/ASP.NET Core · práticas da AWS · práticas do PostgreSQL.

**Referência conceitual, não checklist copiado.** Só entra controle aplicável ao
Prisma RH.

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

> **Regra acrescentada em 31/08/2026, na Fase 8.** **Dígito verificador válido não reserva
> faixa fictícia.** Um CNPJ ou CPF "inventado" que passa na validação pode pertencer a
> alguém — e passar, sozinho, não é prova de nada.
>
> A demo usava `11.222.333/0001-81` e `11.444.777/0001-61`, que *pareciam* inventados. A
> própria consulta à Receita, construída na Fase 8, mostrou que os dois estão registrados:
> uma caixa escolar no RS e uma empreiteira em SP — a primeira com nome e CPF parcial de
> pessoa física no quadro societário.
>
> **Antes de usar um documento em dado de demonstração, confira que ele não existe.** Os
> atuais (`99.999.999/0001-91` e `99.999.998/0001-47`) foram conferidos e voltaram "não
> encontrado".

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
- **enfraquecer ou remover qualquer controle de segurança já implementado** — a lista
  permanente está em §24.3 e §24.5. Se um deles tiver vulnerabilidade real, **documentar
  o problema e informar o responsável antes de alterar**;
- afrouxar CORS, cookie, política de autorização ou filtro global;
- criar rota anônima nova;
- alterar regra legal;
- alterar regra de cálculo;
- ampliar escopo;
- implementar fase futura;
- criar recurso AWS;
- fazer deploy;
- publicar código;
- adicionar IA/LLM ao produto **antes da Fase 11**, ou fora do escopo definido no §37;
- escolher o provedor de IA;
- enviar dado do produto a um provedor de IA;
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
- banco vetorial.

O Prisma RH poderá usar algumas dessas tecnologias no futuro **somente se o produto criar uma necessidade real**.

> **Alteração registrada em 27/08/2026 — IA saiu desta lista.**
>
> "IA generativa" constava aqui como tecnologia sem previsão de entrada. Ela agora tem
> fase própria e aprovada no roadmap — **Fase 11 — Assistente Inteligente / Automação
> com IA** — com escopo, restrições e critérios de aceite definidos no `ROADMAP.md`.
>
> A regra mudou de **"não entra"** para **"não entra antes da Fase 11"**. As regras
> permanentes dessa camada estão no §37 abaixo.
>
> **Banco vetorial continua na lista.** É uma decisão separada, ainda sem fase.

---

# 37. INTELIGÊNCIA ARTIFICIAL NO PRISMA RH

Decisão registrada em **27/08/2026**. A IA é uma tecnologia **futura oficialmente
aprovada** para o produto, com fase própria no roadmap.

## 37.1 Quando pode existir

**Fase 11 — Assistente Inteligente / Automação com IA**, e não antes.

Até lá, nenhum agente pode instalar SDK de IA, criar endpoint de IA, chamar provedor,
adicionar chave de provedor ao ambiente ou criar abstração "para quando a IA chegar".
Conhecer o destino não autoriza antecipá-lo.

A Fase 11 depende das Fases 6 e 7: a IA trabalha sobre inconsistências e workflow já
estruturados. Sem eles, não há o que explicar.

## 37.2 Para que serve

Auxiliar **análise e produtividade** do analista de RH:

- explicar, em linguagem simples, uma inconsistência que o motor determinístico detectou;
- resumir uma folha já processada e analisada;
- transformar uma pergunta em português em filtro ou consulta controlada pela aplicação.

A IA é **copiloto**, nunca autoridade do sistema.

## 37.3 O que a IA nunca é

**Cálculos financeiros e legais permanecem determinísticos, em C#.**

A IA nunca é fonte oficial de INSS, FGTS, IRRF, salário, férias, 13º, rescisão,
encargos, líquido da folha ou qualquer outro valor financeiro ou obrigação legal. Esses
valores continuam vindo de regras versionadas, testáveis e apoiadas em fonte oficial
(§29).

O critério prático: **se o valor entra numa conta, num holerite ou numa obrigação, ele
veio do C#.** Se é frase explicando um valor que o C# já produziu, pode ter vindo da IA —
e precisa estar rotulada como tal na interface.

## 37.4 O que a IA não faz

Não altera salário. Não cria lançamentos. Não fecha nem reabre folha. Não resolve
inconsistência. Não aprova cálculo. Não executa SQL arbitrário. Não muda regra legal.
Não atualiza parâmetro automaticamente. Não toma decisão financeira. **Não modifica
dado algum sem ação explícita e validada do usuário.**

A camada de IA é de **leitura**. Nenhum caminho de código iniciado por resposta de
modelo pode terminar em escrita no banco.

## 37.5 Autorização, multiempresa, auditoria e segurança

Qualquer ação proposta pela IA passa pelas mesmas portas de sempre:

- **autorização por perfil** (§6), aplicada no backend;
- **isolamento por organização** (§5): a consulta gerada continua sob o *global query
  filter*. A IA não amplia o próprio alcance, e o isolamento não depende do bom
  comportamento do modelo;
- **auditoria de negócio** (§26): quando uma sugestão de IA participa de uma decisão,
  isso fica registrado;
- **nada de código arbitrário**: o modelo escolhe dentro de um vocabulário fechado de
  campos e operadores declarado pela aplicação. O que estiver fora é recusado antes de
  virar consulta.

Texto vindo do modelo é **dado, nunca instrução** para a aplicação.

## 37.6 Privacidade dos dados enviados ao modelo

Enviar dado pessoal e trabalhista a um provedor externo é decisão de privacidade, não
detalhe de implementação (§25).

- **Minimização**: só os campos necessários àquela pergunta;
- CPF, endereço, data de nascimento e dado bancário não saem quando o raciocínio não
  depende deles;
- demo pública usa apenas a base fictícia (§24);
- chave do provedor em variável de ambiente ou gerenciador de segredos, **nunca** em
  código, commit ou documentação (§33);
- registrar **que** houve chamada, para qual finalidade e em qual organização — sem
  despejar o conteúdo enviado no log;
- verificar a política de retenção e treinamento do provedor antes de escolhê-lo.

## 37.7 Custo

**Nenhuma implementação de IA pode gerar custo recorrente sem análise e autorização
explícitas.**

A cobrança por token de um provedor externo **não** faz parte do orçamento AWS — vale a
mesma regra do §14 para API de terceiro. O teto de **US$ 6,50/mês em AWS** do Prisma RH
(§16) permanece inalterado, e qualquer recurso AWS usado pela IA entra nesse teto e
segue o ritual de sempre: listar, justificar, estimar, buscar alternativa gratuita,
pedir autorização.

## 37.8 Provedor

> **Decisão registrada em 01/09/2026, ao executar a Fase 11.** Antes esta seção dizia
> "não escolhido, de propósito". A escolha agora está feita.

**Google Gemini**, modelo `gemini-3.5-flash-lite`, escolhido pelo responsável do projeto,
que forneceu a chave. Chamado direto pela API do provedor, e **não** via AWS Bedrock — o
que mantém o teto de US$ 6,50/mês da AWS (§16) inteiramente livre para as Fases 9 e 10.

O modelo é o mais barato da família, e a tarefa justifica: explicar em português um
resultado que o C# já produziu não exige raciocínio complexo. Um modelo maior custaria
mais para produzir texto mais longo, que o analista não lê.

⚠️ **O nome do modelo envelhece sozinho.** `gemini-2.5-flash-lite` foi a primeira escolha
e já não aceita contas novas — o provedor devolve `404` indicando o substituto. O modelo
continuava aparecendo na listagem de modelos: só a chamada real revelou a aposentadoria.
Daí duas consequências permanentes:

1. **A fase tem verificação contra o provedor real**, e não apenas contra dublês. Suíte
   verde com provedor falso não prova que o modelo existe.
2. **O produto não pode depender do modelo estar de pé.** Quando ele cair, a resposta vira
   `Indisponivel`, a tela mostra o aviso e o achado do motor determinístico permanece
   legível. É o `§1` na prática: o Prisma RH não depende de outro sistema para funcionar.

⚠️ **Não foi possível confirmar se o projeto Google tem faturamento ativado** — a API não
informa. Os limites do `OrcamentoIa` são dimensionados para o **pior caso**: mesmo com
cobrança por token ativa, o gasto de uso de portfólio fica em centavos por mês.

**Retenção e treinamento do provedor:** a política do Gemini distingue o nível gratuito,
onde o conteúdo **pode** ser usado para melhorar os produtos do Google, do nível pago,
onde não é. Como não se sabe qual está valendo nesta conta, vale a suposição mais
conservadora — **o que for enviado pode ser retido** —, e é por isso que a minimização do
`§37.6` não é formalidade: nome, CPF e matrícula **não saem**. Antes de qualquer uso com
dado real, essa política precisa ser confirmada no console do provedor.

A arquitetura permite trocar de provedor **sem tocar no domínio**: o `ClienteGemini` vive
em `Infraestrutura/Ia`, e o domínio não sabe que IA existe.

## 37.9 Ameaças específicas da camada de IA

A IA traz uma classe de ameaça que nenhuma outra parte do sistema tem: **um componente
que aceita linguagem natural e produz algo que o sistema vai usar.** Validação comum não
resolve, porque a entrada é legítima por definição.

A IA **nunca** recebe credencial, secret ou token. Nunca executa SQL arbitrário nem
comando. Nunca altera folha por conta própria. E o dado recuperado para ela respeita **as
mesmas políticas de autorização** do resto do sistema, com o perfil de quem perguntou —
nunca com um perfil de serviço privilegiado.

| Ameaça | Defesa |
|---|---|
| **Prompt injection direto** | A saída é estruturada, com vocabulário fechado; o que estiver fora é recusado antes de virar consulta. Instrução do usuário não amplia o que o sistema permite. |
| **Prompt injection indireto** | Instrução escondida em dado que o sistema já guarda — nome de funcionário, justificativa, célula de planilha, campo de integração. **Todo texto vindo do banco é dado, jamais instrução**, inclusive quando parece uma ordem. |
| **Exfiltração de dados** | Minimização na entrada: o que não foi enviado não pode vazar na resposta. |
| **Cross-tenant leakage** | Uma chamada, um tenant. A consulta roda sob o filtro global — o isolamento é arquitetural, não depende do modelo se comportar. |
| **Tool abuse** | A camada de IA é de leitura. Nenhum caminho iniciado por resposta de modelo termina em escrita no banco. |
| **Respostas não confiáveis e alucinação** | Nenhum valor financeiro tem origem no modelo. Toda saída é rotulada como gerada por IA e passível de erro. |
| **Custo abusivo** | Cobrança por token torna o abuso lucrativo para quem ataca e caro para quem mantém. Teto de contexto, teto de chamadas por organização e por usuário, cache do resumo já gerado, alerta de gasto. |

Detalhamento operacional no Security Gate da Fase 11 do `ROADMAP.md`.

---

# 38. ROADMAP OBRIGATÓRIO

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

## FASE 11 — Assistente Inteligente / Automação com IA

Primeira fase em que a IA pode existir no produto. Regras permanentes desta camada: §37.

Entregas:

- assistente de inconsistências: explica, resume e sugere o que conferir;
- resumo executivo da folha já processada e analisada;
- consulta em linguagem natural convertida em filtro controlado pela aplicação.

O motor de cálculo permanece **100% determinístico**. A IA explica, não calcula.

Depende das Fases 6 e 7 (dados estruturados de análise e workflow), reusa o padrão de
integração externa da Fase 8 e vem antes do hardening, para ser auditada nele.

Fora de escopo: IA calculando ou conferindo valor de folha, agente que executa ações
sozinho, geração de regra de análise pelo modelo, *fine-tuning* com dado de cliente,
banco vetorial/RAG.

---

# 39. DADOS DE DEMONSTRAÇÃO

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

# 40. DEFINITION OF DONE

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
- o agente consegue explicar objetivamente o que mudou;
- **a Definition of Done de segurança abaixo foi cumprida.**

## 40.1 Definition of Done de segurança

Vale para toda tarefa que toque endpoint, dado de tenant, autenticação, autorização,
entrada externa, arquivo, segredo, dependência ou infraestrutura.

**Uma tarefa relevante NÃO está concluída se:**

- [ ] a **autorização** não foi analisada — quem pode fazer isso, e por quê;
- [ ] a **multi-tenancy** não foi analisada — este caminho pode alcançar outra organização?
- [ ] **entrada externa** não foi validada no backend;
- [ ] **dado sensível foi exposto** além do necessário — em resposta, log, export ou erro;
- [ ] um **secret** foi incluído em código, commit, log, bundle ou documentação;
- [ ] um **endpoint novo** ficou sem política de acesso declarada;
- [ ] um **upload** ficou sem limite de tamanho, de quantidade e validação de conteúdo;
- [ ] uma **dependência com vulnerabilidade conhecida** foi introduzida;
- [ ] **logs vazam** token, senha, cookie, CPF completo ou payload sensível;
- [ ] **testes críticos de segurança falham** — isolamento e autorização inclusos;
- [ ] uma **listagem nova** ficou sem paginação com teto;
- [ ] um controle de segurança existente foi **enfraquecido** sem decisão registrada.

Quando um item não se aplica, escrever **"não se aplica"** e o motivo. A resposta
explícita é o registro de que a pergunta foi feita — e é ela que distingue "verifiquei"
de "não pensei nisso".

---

# 41. CHECKLIST ANTES DE ALTERAR CÓDIGO

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
- [ ] **Li o Security Gate da fase atual** (`ROADMAP.md §4.1` e o gate da fase)?
- [ ] **Esta tarefa cria superfície nova?** Endpoint, entrada externa, arquivo, chamada
      para fora, rota anônima, segredo, dependência.
- [ ] Se cria, **fiz o threat model curto** (§24.27) antes de escrever código?
- [ ] **Este caminho toca dado de outra organização?**
- [ ] Estou **enfraquecendo** algum controle de segurança já existente?

Se qualquer resposta indicar risco ou conflito, interromper e pedir decisão.

---

# 42. CHECKLIST APÓS ALTERAR CÓDIGO

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
- [ ] **Definition of Done de segurança (§40.1) percorrida**, com "não se aplica"
      escrito onde não se aplica.
- [ ] **Teste de isolamento** existe para toda funcionalidade nova que manipule dado de
      tenant.
- [ ] **Toda rota nova tem política de acesso** declarada.
- [ ] **Toda listagem nova tem paginação com teto.**
- [ ] Pendência de segurança encontrada foi **registrada e datada** no `CLAUDE.md §24.19`
      ou no Security Gate da fase — pendência não registrada invalida o gate.

---

# 43. FILOSOFIA DE IMPLEMENTAÇÃO

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

# 44. OBJETIVO DE APRENDIZADO

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

# 45. AUTORIDADE DESTE DOCUMENTO

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
