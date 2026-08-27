using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrismaRH.Dominio.Folha;

namespace PrismaRH.Infraestrutura.Persistencia.Configuracoes;

public sealed class RubricaConfiguracao : IEntityTypeConfiguration<Rubrica>
{
    public void Configure(EntityTypeBuilder<Rubrica> builder)
    {
        builder.ToTable("rubricas");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(r => r.IdOrganizacao).HasColumnName("id_organizacao").IsRequired();

        builder.Property(r => r.Codigo)
            .HasColumnName("codigo")
            .HasMaxLength(Rubrica.TamanhoMaximoCodigo)
            .IsRequired();

        builder.Property(r => r.Nome)
            .HasColumnName("nome")
            .HasMaxLength(Rubrica.TamanhoMaximoNome)
            .IsRequired();

        builder.Property(r => r.Tipo).HasColumnName("tipo").HasConversion<int>().IsRequired();
        builder.Property(r => r.Estrategia).HasColumnName("estrategia").HasConversion<int>().IsRequired();
        // Enum de bits numa coluna so: a rubrica que entra em INSS e FGTS
        // guarda 3. Ver BaseCalculo para por que os valores sao potencias de dois.
        builder.Property(r => r.BasesIncidentes)
            .HasColumnName("bases_incidentes").HasConversion<int>().IsRequired();

        builder.Property(r => r.Ativa).HasColumnName("ativa").IsRequired();
        builder.Property(r => r.CriadoEm).HasColumnName("criado_em").IsRequired();

        builder.HasIndex(r => new { r.IdOrganizacao, r.Codigo })
            .HasDatabaseName("ux_rubricas_organizacao_codigo")
            .IsUnique();

        // No maximo UMA rubrica de salario-base ativa por organizacao.
        //
        // Sem isso, o calculo teria que escolher entre duas rubricas de
        // salario e escolheria em silencio - metade da empresa poderia sair
        // com o codigo errado no holerite. O indice e parcial porque a
        // restricao so vale para as ativas: inativar e criar outra continua
        // sendo permitido.
        builder.HasIndex(r => r.IdOrganizacao)
            .HasDatabaseName("ux_rubricas_salario_base_ativa")
            .IsUnique()
            .HasFilter($"estrategia = {(int)EstrategiaRubrica.SalarioBaseProporcional} AND ativa");

        // Mesma razao para o INSS: duas rubricas de INSS ativas fariam a folha
        // descontar a contribuicao duas vezes, e o liquido sairia menor sem
        // que nenhuma linha parecesse errada isoladamente.
        builder.HasIndex(r => r.IdOrganizacao)
            .HasDatabaseName("ux_rubricas_inss_ativa")
            .IsUnique()
            .HasFilter($"estrategia = {(int)EstrategiaRubrica.InssProgressivo} AND ativa");

        // E para o FGTS: duas rubricas ativas dobrariam o deposito informado.
        // O erro seria pior que o do INSS por ser silencioso - como o FGTS nao
        // entra no liquido, o holerite continuaria fechando certo enquanto a
        // guia de recolhimento sairia com o dobro.
        builder.HasIndex(r => r.IdOrganizacao)
            .HasDatabaseName("ux_rubricas_fgts_ativa")
            .IsUnique()
            .HasFilter($"estrategia = {(int)EstrategiaRubrica.FgtsMensal} AND ativa");
    }
}
