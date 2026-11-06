using Microsoft.EntityFrameworkCore;

namespace GeneralExpenses.Api.Data;

public sealed class ExpensesDbContext(DbContextOptions<ExpensesDbContext> options) : DbContext(options)
{
    public DbSet<ReceiptEntity> Receipts => Set<ReceiptEntity>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var receipt = model.Entity<ReceiptEntity>();
        receipt.ToTable("receipts");
        receipt.HasKey(x => x.Id);
        receipt.Property(x => x.Revision).IsConcurrencyToken();
        receipt.HasMany(x => x.Participants).WithOne().HasForeignKey(x => x.ReceiptId);
        receipt.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.ReceiptId);

        var person = model.Entity<ParticipantEntity>();
        person.ToTable("participants");
        person.HasKey(x => new { x.ReceiptId, x.Id });
        person.Property(x => x.Name).HasMaxLength(100);
        person.Property(x => x.Initial).HasMaxLength(10);

        var item = model.Entity<ItemEntity>();
        item.ToTable("items", table => table.HasCheckConstraint("ck_item_amount", "\"Amount\" BETWEEN 1 AND 100000000"));
        item.HasKey(x => new { x.ReceiptId, x.Id });
        item.Property(x => x.Name).HasMaxLength(100);
        // У одного чека может быть лишь одна позиция, ожидающая отмены удаления.
        item.HasIndex(x => x.ReceiptId).IsUnique().HasFilter("\"IsDeleted\" = TRUE");
        item.HasMany(x => x.Assignments).WithOne().HasForeignKey(x => new { x.ReceiptId, x.ItemId });

        var assignment = model.Entity<AssignmentEntity>();
        assignment.ToTable("assignments");
        assignment.HasKey(x => new { x.ReceiptId, x.ItemId, x.ParticipantId });
        assignment.HasOne<ParticipantEntity>().WithMany().HasForeignKey(x => new { x.ReceiptId, Id = x.ParticipantId });
        // Составные ключи не позволят связать блюдо одного чека с участником другого.
    }
}
