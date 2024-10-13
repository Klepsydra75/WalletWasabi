using NBitcoin;

namespace WalletWasabi.BitcoinCore.Rpc.Models;

public abstract record VerboseInputInfo
{
	public record Coinbase(string Message) : VerboseInputInfo;

	public record Full(OutPoint OutPoint, VerboseOutputInfo PrevOut) : VerboseInputInfo;

	public record None(OutPoint Outpoint) : VerboseInputInfo;
}
