using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MestoBeach.Orders.Infrastructure.Persistence;

/// <summary>
/// Proyección de solo lectura de <c>tables</c> (propiedad del módulo Sessions; PRD-019, ADR-038):
/// solo la clase de puesto (<c>kind</c>: TABLE | HIGH_TABLE | RESERVADO), que fija el snapshot
/// <c>orders.priority</c> al crear la comanda. Excluida de las migraciones de Orders.
/// </summary>
public sealed class TableReadModel
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Kind { get; set; } = "TABLE";
}

public sealed class TableReadModelConfiguration : IEntityTypeConfiguration<TableReadModel>
{
    public void Configure(EntityTypeBuilder<TableReadModel> b)
    {
        b.ToTable("tables", t => t.ExcludeFromMigrations());
        b.HasKey(t => t.Id);
        b.Property(t => t.Id).HasColumnName("id");
        b.Property(t => t.TenantId).HasColumnName("tenant_id");
        b.Property(t => t.Kind).HasColumnName("kind").HasMaxLength(16);
    }
}
