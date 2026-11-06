using Neo;
using Neo.VM;
using NeoOrder.OneGate.Services.RPC;
using Xunit;

public class TransferScriptTests
{
    [Theory]
    [InlineData(false, true, VMState.HALT)]
    [InlineData(false, false, VMState.FAULT)]
    [InlineData(true, true, VMState.HALT)]
    [InlineData(true, false, VMState.FAULT)]
    public void TransferMustReturnTrue(bool nft, bool result, VMState expected)
    {
        Assert.Equal(expected, Execute(Script(nft), result));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangedResultAfterSimulationFaults(bool nft)
    {
        byte[] script = Script(nft);
        Assert.Equal(VMState.HALT, Execute(script, true));
        Assert.Equal(VMState.FAULT, Execute(script, false));
        Assert.Equal(VMState.FAULT, Execute(script, null));
    }

    static byte[] Script(bool nft) => nft
        ? TransferScript.Nep11(UInt160.Zero, [1], UInt160.Zero, null)
        : TransferScript.Nep17(UInt160.Zero, UInt160.Zero, UInt160.Zero, 1, null);

    // Run the real emitted script in Neo VM, substituting only the contract syscall.
    static VMState Execute(byte[] script, bool? contractResult)
    {
        var table = new JumpTable();
        table[OpCode.SYSCALL] = (engine, instruction) =>
        {
            engine.Pop(); // contract hash
            engine.Pop(); // method
            engine.Pop(); // call flags
            engine.Pop(); // arguments
            if (!contractResult.HasValue) throw new InvalidOperationException("Contract FAULT");
            engine.Push(contractResult.Value);
        };
        using var engine = new ExecutionEngine(table);
        engine.LoadScript(script);
        return engine.Execute();
    }
}
