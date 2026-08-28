using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrismaRH.Dominio.Empresas;
using PrismaRH.Dominio.Folha;

namespace PrismaRH.Infraestrutura.Persistencia.Configuracoes;

public sealed class FolhaPagamentoConfiguracao : IEntityTypeConfiguration<FolhaPagamento>
{
    public void Configure(EntityTypeBuilder<FolhaPagamento> builder)
    {
        builder.ToTable("folhas_pagamento");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(f => f.IdOrganizacao).HasColumnName("id_organizacao").IsRequired();
        builder.Property(f => f.IdEmpresa).HasColumnName("id_empresa").IsRequired();

        // Competencia vira o inteiro 202608. Uma coluna so, ordenavel e
        // indexavel: 202512 < 202601 sem conversao nenhuma. Duas colunas
        // exigiriam ORDER BY ano, mes em toda consulta, e bastaria esquecer o
        // segundo campo para janeiro aparecer antes de dezembro.
        builder.Property(f => f.Competencia)
            .HasColumnName("competencia")
            .HasConversion(c => c.Codigo, codigo => Competencia.DoCodigo(codigo))
            .IsRequired();

        builder.Property(f => f.Situacao).HasColumnName("situacao").HasConversion<int>().IsRequired();
        builder.Property(f => f.VersaoCalculo).HasColumnName("versao_calculo").IsRequired();
        builder.Property(f => f.CriadoEm).HasColumnName("criado_em").IsRequired();
        builder.Property(f => f.CalculadaEm).HasColumnName("calculada_em");
        builder.Property(f => f.FechadaEm).HasColumnName("fechada_em");

        builder.Property(f => f.TotalProventos).HasColumnName("total_proventos").HasPrecision(14, 2).IsRequired();
        builder.Property(f => f.TotalDescontos).HasColumnName("total_descontos").HasPrecision(14, 2).IsRequired();
        builder.Property(f => f.TotalLiquido).HasColumnName("total_liquido").HasPrecision(14, 2).IsRequired();

        builder.HasOne<Empresa>()
            .WithMany()
            .HasForeignKey(f => f.IdEmpresa)
            .OnDelete(DeleteBehavior.Restrict);

        // O default existe para o BACKFILL: as folhas criadas antes da Fase 4E
        // sao todas mensais, e a migration as marca assim.
        //
        // O EF avisa que o default do banco so entra quando a propriedade vale
        // 0, o CLR default. Aqui isso nunca acontece: TipoFolha nao tem membro
        // 0 (Mensal = 1) e o construtor recusa valor nao definido. Ou seja, o
        // default nunca decide nada em INSERT novo - so preencheu o passado.
        builder.Property(f => f.Tipo)
            .HasColumnName("tipo").HasConversion<int>().IsRequired()
            .HasDefaultValue(TipoFolha.Mensal);

        // Uma folha por empresa, por competencia E POR TIPO. Abrir agosto
        // duas vezes do mesmo tipo produziria dois totais divergentes para o
        // mesmo mes, e nada no sistema diria qual e o verdadeiro.
        //
        // A coluna do tipo entrou na Fase 4E: a mesma empresa pode ter, em
        // agosto, a folha mensal E a de ferias. Sem ela, a segunda seria
        // recusada por um indice que nao existia para isso.
        builder.HasIndex(f => new { f.IdEmpresa, f.Competencia, f.Tipo })
            .HasDatabaseName("ux_folhas_empresa_competencia_tipo")
            .IsUnique();

        builder.HasMany(f => f.Funcionarios)
            .WithOne()
            .HasForeignKey(ff => ff.IdFolha)
            .OnDelete(DeleteBehavior.Cascade);

        // O EF le e escreve a colecao pelo CAMPO, nunca pela propriedade. A
        // propriedade e somente leitura e existe para o resto do sistema; se o
        // EF tentasse usa-la para adicionar um item, nao teria onde adicionar.
        builder.Navigation(f => f.Funcionarios)
            .HasField("_funcionarios")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
