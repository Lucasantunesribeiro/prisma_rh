import { enviar, obter, remover } from './cliente'

// ---------------------------------------------------------------- cargos

export interface Cargo {
  id: string
  codigo: string
  nome: string
  ativo: boolean
}

export const listarCargos = (): Promise<Cargo[]> => obter('/api/cargos')

export const criarCargo = (dados: { codigo: string; nome: string }): Promise<Cargo> =>
  enviar('/api/cargos', dados)

// ---------------------------------------------------------------- funcionários

export interface Funcionario {
  id: string
  nome: string
  /** Na listagem vem mascarado; no detalhe, completo. */
  cpf: string
  cpfFormatado: string
  dataNascimento: string
  ativo: boolean
}

export interface PaginaFuncionarios {
  total: number
  pagina: number
  tamanho: number
  itens: Funcionario[]
}

export interface FiltroFuncionarios {
  nome?: string
  cpf?: string
  ativo?: boolean
}

export function listarFuncionarios(filtro: FiltroFuncionarios = {}): Promise<PaginaFuncionarios> {
  const parametros = new URLSearchParams()

  if (filtro.nome?.trim()) parametros.set('nome', filtro.nome.trim())
  if (filtro.cpf?.trim()) parametros.set('cpf', filtro.cpf.trim())
  if (filtro.ativo !== undefined) parametros.set('ativo', String(filtro.ativo))

  const consulta = parametros.toString()
  return obter(`/api/funcionarios${consulta ? `?${consulta}` : ''}`)
}

export const obterFuncionario = (id: string): Promise<Funcionario> =>
  obter(`/api/funcionarios/${id}`)

export const criarFuncionario = (dados: {
  nome: string
  cpf: string
  dataNascimento: string
}): Promise<Funcionario> => enviar('/api/funcionarios', dados)

// ---------------------------------------------------------------- contratos

export type SituacaoContrato = 'Ativo' | 'Desligado'

export type MotivoVigencia =
  | 'Admissao'
  | 'AlteracaoSalarial'
  | 'MudancaCargo'
  | 'Transferencia'
  | 'AlteracaoJornada'
  | 'Desligamento'

export const ROTULO_MOTIVO: Record<MotivoVigencia, string> = {
  Admissao: 'Admissão',
  AlteracaoSalarial: 'Alteração salarial',
  MudancaCargo: 'Mudança de cargo',
  Transferencia: 'Transferência',
  AlteracaoJornada: 'Alteração de jornada',
  Desligamento: 'Desligamento',
}

export interface Vigencia {
  id: string
  validoDe: string
  validoAte: string | null
  salario: number
  idCargo: string
  idEstabelecimento: string
  jornadaMensalHoras: number
  motivo: MotivoVigencia
}

export interface Contrato {
  id: string
  idFuncionario: string
  idEmpresa: string
  matricula: string
  dataAdmissao: string
  dataDesligamento: string | null
  situacao: SituacaoContrato
  vigenciaAtual: Vigencia | null
}

export const listarContratos = (idFuncionario: string): Promise<Contrato[]> =>
  obter(`/api/funcionarios/${idFuncionario}/contratos`)

export const criarContrato = (
  idFuncionario: string,
  dados: {
    idEmpresa: string
    matricula: string
    dataAdmissao: string
    salarioInicial: number
    idCargo: string
    idEstabelecimento: string
    jornadaMensalHoras: number
  },
): Promise<Contrato> => enviar(`/api/funcionarios/${idFuncionario}/contratos`, dados)

export const listarVigencias = (idContrato: string): Promise<Vigencia[]> =>
  obter(`/api/contratos/${idContrato}/vigencias`)

export const registrarAlteracao = (
  idContrato: string,
  dados: {
    validoDe: string
    salario: number
    idCargo: string
    idEstabelecimento: string
    jornadaMensalHoras: number
    motivo: MotivoVigencia
  },
): Promise<Vigencia> => enviar(`/api/contratos/${idContrato}/vigencias`, dados)

export const desligar = (idContrato: string, dataDesligamento: string): Promise<Contrato> =>
  enviar(`/api/contratos/${idContrato}/desligamento`, { dataDesligamento })

// -------------------------------------------------------------- dependentes

export type RelacaoDependente =
  | 'Conjuge'
  | 'Companheiro'
  | 'Filho'
  | 'Enteado'
  | 'Irmao'
  | 'Neto'
  | 'Pai'
  | 'Mae'
  | 'Avo'
  | 'Tutelado'
  | 'Outro'

export const ROTULO_RELACAO: Record<RelacaoDependente, string> = {
  Conjuge: 'Cônjuge',
  Companheiro: 'Companheiro(a)',
  Filho: 'Filho(a)',
  Enteado: 'Enteado(a)',
  Irmao: 'Irmão(ã)',
  Neto: 'Neto(a)',
  Pai: 'Pai',
  Mae: 'Mãe',
  Avo: 'Avô/Avó',
  Tutelado: 'Tutelado(a)',
  Outro: 'Outro',
}

export interface Dependente {
  id: string
  idFuncionario: string
  nome: string
  dataNascimento: string
  relacao: RelacaoDependente
  /** Derivado do período: sem início, não abate IRRF. */
  dedutivelIrrf: boolean
  inicioDeducaoIrrf: string | null
  fimDeducaoIrrf: string | null
}

export interface DadosDependente {
  nome: string
  dataNascimento: string
  relacao: RelacaoDependente
  inicioDeducaoIrrf: string | null
  fimDeducaoIrrf: string | null
}

export const listarDependentes = (idFuncionario: string): Promise<Dependente[]> =>
  obter(`/api/funcionarios/${idFuncionario}/dependentes`)

export const criarDependente = (
  idFuncionario: string,
  dados: DadosDependente,
): Promise<Dependente> => enviar(`/api/funcionarios/${idFuncionario}/dependentes`, dados)

export const atualizarDependente = (
  idFuncionario: string,
  id: string,
  dados: DadosDependente,
): Promise<Dependente> =>
  enviar(`/api/funcionarios/${idFuncionario}/dependentes/${id}`, dados, 'PUT')

export const removerDependente = (idFuncionario: string, id: string): Promise<void> =>
  remover(`/api/funcionarios/${idFuncionario}/dependentes/${id}`)

// ---------------------------------------------------------------- formatação

const MOEDA = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' })

export const formatarSalario = (valor: number): string => MOEDA.format(valor)

/**
 * Datas civis vêm da API como "2026-08-23", sem fuso.
 * `new Date('2026-08-23')` interpretaria como UTC e, no Brasil, exibiria o dia
 * anterior. Por isso a formatação é feita na mão, sem passar por Date.
 */
export function formatarData(iso: string | null): string {
  if (!iso) return '—'

  const [ano, mes, dia] = iso.slice(0, 10).split('-')
  return `${dia}/${mes}/${ano}`
}
