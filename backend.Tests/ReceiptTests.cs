using GeneralExpenses.Api;
using Xunit;

public class ReceiptTests
{
    private static ChangeRequest Version(ReceiptDto receipt) => new() { Revision = receipt.Revision, InstanceId = receipt.InstanceId };
    private static ItemRequest Item(ReceiptDto receipt, string name, long amount) => new() { Revision = receipt.Revision, InstanceId = receipt.InstanceId, Name = name, Amount = amount };
    private static SelectionRequest Select(ReceiptDto receipt) => new() { Revision = receipt.Revision, InstanceId = receipt.InstanceId, Selected = true };

    [Fact]
    public void RemainderIsDistributedWithoutLosingKopecks()
    {
        Participant[] people = [new("a", "А", "А"), new("b", "Б", "Б"), new("c", "В", "В")];
        var result = ReceiptCalculator.Calculate([new("one", "Пицца", "", 10000)], new Dictionary<string, string[]> { ["one"] = ["c", "b", "a"] }, people);
        Assert.Equal(3334, result.Totals["a"]);
        Assert.Equal(3333, result.Totals["b"]);
        Assert.Equal(result.Total, result.Totals.Values.Sum());
    }

    [Fact]
    public void EditPreservesOwnersAndDeleteRemovesPosition()
    {
        var store = new ReceiptState();
        var state = store.Select("pizza", "anya", Select(store.Read()));
        state = store.Select("pizza", "boris", Select(state));
        state = store.Edit("pizza", Item(state, "Общая пицца", 100001));
        Assert.Equal(50001, state.Result.Totals["anya"]);
        Assert.Equal(50000, state.Result.Totals["boris"]);
        state = store.Delete("pizza", Version(state));
        Assert.False(state.Assignments.ContainsKey("pizza"));
        Assert.Equal(0, state.Result.Totals["anya"]);
        Assert.DoesNotContain(state.Items, i => i.Id == "pizza");
    }

    [Fact]
    public void StaleRequestDoesNotApplyTwice()
    {
        var store = new ReceiptState();
        var request = Item(store.Read(), "Десерт", 100);
        var first = store.Add(request);
        Assert.Equal(409, Assert.Throws<ReceiptException>(() => store.Add(request)).Status);
        Assert.Equal(first.Items.Length, store.Read().Items.Length);
        var otherStore = new ReceiptState();
        Assert.Equal(409, Assert.Throws<ReceiptException>(() => otherStore.Add(request)).Status);
    }

    [Theory]
    [InlineData(" ", 100)]
    [InlineData("Тест", 0)]
    [InlineData("Тест", -1)]
    [InlineData("Тест", 100000001)]
    public void InvalidItemDoesNotChangeReceipt(string name, long amount)
    {
        var store = new ReceiptState();
        var state = store.Read();
        Assert.Equal(400, Assert.Throws<ReceiptException>(() => store.Add(Item(state, name, amount))).Status);
        Assert.Equal(state.Revision, store.Read().Revision);
    }

    [Fact]
    public void EmptyReceiptAndResetAreConsistent()
    {
        var store = new ReceiptState();
        var state = store.Select("pizza", "anya", Select(store.Read()));
        state = store.Delete("pizza", Version(state));
        state = store.Reset(Version(state));
        Assert.False(state.Assignments.ContainsKey("pizza"));
        foreach (var item in state.Items) state = store.Delete(item.Id, Version(state));
        Assert.Empty(state.Items);
        Assert.Equal(0, state.Result.Total);
        Assert.Equal(0, state.Result.Unassigned);
        state = store.Add(Item(state, "Новый чек", 1));
        Assert.Equal(1, state.Result.Unassigned);
    }
}

