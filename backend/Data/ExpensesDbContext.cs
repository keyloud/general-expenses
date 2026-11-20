using Microsoft.EntityFrameworkCore;

namespace GeneralExpenses.Api.Data;

public sealed class ExpensesDbContext(DbContextOptions<ExpensesDbContext> options) : DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<ReceiptMessage> Messages => Set<ReceiptMessage>();
    public DbSet<ReceiptEntity> Receipts => Set<ReceiptEntity>();
    public DbSet<DeletedReceiptEntity> DeletedReceipts => Set<DeletedReceiptEntity>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<DeletedReceiptEntity>().ToTable("deleted_receipts").HasKey(r => r.Id);
        var message = model.Entity<ReceiptMessage>();
        message.ToTable("receipt_messages");
        message.HasOne<ReceiptEntity>().WithMany().HasForeignKey(m => m.ReceiptId);
        message.HasIndex(m => m.NextAttempt);
        model.Entity<ItemEntity>().Property(i => i.Quantity).HasDefaultValue(1);
        model.Entity<AssignmentEntity>().Property(a => a.Count).HasDefaultValue(1);
        var user = model.Entity<UserEntity>();
        user.ToTable("users");
        user.HasKey(x => x.Id);
        user.Property(x => x.Id).ValueGeneratedNever();
        user.Property(x => x.Name).HasMaxLength(256);
        user.Property(x => x.Username).HasMaxLength(64);
        // Старые счета принадлежат этому аккаунту, а не первому посетителю Telegram.
        user.HasData(new UserEntity { Id = -1, Name = "Локальный разработчик" });

        var receipt = model.Entity<ReceiptEntity>();
        receipt.Property(x => x.OwnerUserId).HasDefaultValue(-1L);
        receipt.HasOne<UserEntity>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        receipt.ToTable("receipts"); // Настраиваем таблицу
        receipt.HasKey(x => x.Id); // Настраиваем первичный ключ
        receipt.Property(x => x.Title).HasMaxLength(100).HasDefaultValue("Ужин с друзьями"); // Настраиваем столбец с ограничением длины и значением по умолчанию
        receipt.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP"); // Настраиваем столбец с значением по умолчанию в виде текущей временной метки
        receipt.HasIndex(x => x.CreatedAt); // Настраиваем индекс по столбцу CreatedAt для ускорения поиска
        receipt.Property(x => x.Revision).IsConcurrencyToken(); // Настраиваем столбец Revision как токен конкурентного доступа для предотвращения конфликтов при параллельных изменениях
        receipt.HasMany(x => x.Participants).WithOne().HasForeignKey(x => x.ReceiptId); // Настраиваем отношение "один ко многим" между ReceiptEntity и ParticipantEntity, где ReceiptId является внешним ключом в таблице participants
        receipt.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.ReceiptId); // Настраиваем отношение "один ко многим" между ReceiptEntity и ItemEntity, где ReceiptId является внешним ключом в таблице items

        var person = model.Entity<ParticipantEntity>();
        person.HasOne<UserEntity>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        person.HasIndex(x => new { x.ReceiptId, x.UserId }).IsUnique();
        person.Property(x => x.InviteHash).HasMaxLength(64);
        person.HasIndex(x => x.InviteHash).IsUnique();
        person.ToTable("participants"); // Настраиваем таблицу participants
        person.HasKey(x => new { x.ReceiptId, x.Id }); // Настраиваем составной первичный ключ, состоящий из ReceiptId и Id
        person.Property(x => x.Name).HasMaxLength(100);
        person.Property(x => x.Initial).HasMaxLength(10);

        var item = model.Entity<ItemEntity>();
        item.ToTable("items", table => table.HasCheckConstraint("ck_item_amount", "\"Amount\" BETWEEN 1 AND 100000000")); // Настраиваем таблицу items с ограничением на столбец Amount, чтобы его значение было между 1 и 100000000
        item.HasKey(x => new { x.ReceiptId, x.Id });
        item.Property(x => x.Name).HasMaxLength(100);
        item.HasMany(x => x.Assignments).WithOne().HasForeignKey(x => new { x.ReceiptId, x.ItemId });

        var assignment = model.Entity<AssignmentEntity>();
        assignment.ToTable("assignments");
        assignment.HasKey(x => new { x.ReceiptId, x.ItemId, x.ParticipantId });
        assignment.HasOne<ParticipantEntity>().WithMany().HasForeignKey(x => new { x.ReceiptId, Id = x.ParticipantId });
        // Составные ключи не позволят связать блюдо одного чека с участником другого.
    }
}
