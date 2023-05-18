using DynamicData;
using DynamicData.Binding;
using NBitcoin;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.Models.Wallets;

public partial class WalletListModel : ReactiveObject, IWalletListModel
{
	private ReadOnlyObservableCollection<IWalletModel> _wallets;

	public WalletListModel()
	{
		// Convert the Wallet Manager's contents into an observable stream of IWalletModels.
		Wallets =
			Observable.Return(Unit.Default)
					  .Merge(Observable.FromEventPattern<Wallet>(Services.WalletManager, nameof(WalletManager.WalletAdded))
									   .Select(_ => Unit.Default))
					  .ObserveOn(RxApp.MainThreadScheduler)
					  .SelectMany(_ => Services.WalletManager.GetWallets())
					  .ToObservableChangeSet(x => x.WalletName)
					  .Transform(wallet => new WalletModel(wallet))

					  // Refresh the collection when logged in.
					  .AutoRefresh(x => x.IsLoggedIn)

					  // Sort the list to put the most recently logged in wallet to the top.
					  .Sort(SortExpressionComparer<IWalletModel>.Descending(i => i.Auth.IsLoggedIn).ThenByAscending(x => x.Name))
					  .Transform(x => x as IWalletModel);

		// Materialize the Wallet list to determine the default wallet.
		Wallets
			.Bind(out _wallets)
			.Subscribe();

		DefaultWallet =
			_wallets.FirstOrDefault(item => item.Name == Services.UiConfig.LastSelectedWallet)
			?? _wallets.FirstOrDefault();
	}

	public IObservable<IChangeSet<IWalletModel, string>> Wallets { get; }

	public IWalletModel? DefaultWallet { get; }

	private KeyPath AccountKeyPath { get; } = KeyManager.GetAccountKeyPath(Services.WalletManager.Network, ScriptPubKeyType.Segwit);

	public bool HasWallet => Services.WalletManager.HasWallet();

	public async Task<IWalletSettingsModel> RecoverWalletAsync(string walletName, string password, Mnemonic mnemonic, int minGapLimit)
	{
		var keyManager = await Task.Run(() =>
		{
			var walletFilePath = Services.WalletManager.WalletDirectories.GetWalletFilePaths(walletName).walletFilePath;

			var result = KeyManager.Recover(
				mnemonic,
				password,
				Services.WalletManager.Network,
				AccountKeyPath,
				null,
				"", // Make sure it is not saved into a file yet.
				minGapLimit);

			result.AutoCoinJoin = true;

			// Set the filepath but we will only write the file later when the Ui workflow is done.
			result.SetFilePath(walletFilePath);

			return result;
		});

		return new WalletSettingsModel(keyManager, true);
	}

	public async Task<IWalletSettingsModel> CreateNewWalletAsync(string walletName, string password, Mnemonic mnemonic)
	{
		var (keyManager, _) = await Task.Run(
				() =>
				{
					var walletGenerator = new WalletGenerator(
						Services.WalletManager.WalletDirectories.WalletsDir,
						Services.WalletManager.Network)
					{
						TipHeight = Services.BitcoinStore.SmartHeaderChain.TipHeight
					};
					return walletGenerator.GenerateWallet(walletName, password, mnemonic);
				});

		return new WalletSettingsModel(keyManager, true);
	}

	public IWalletModel SaveWallet(IWalletSettingsModel walletSettings)
	{
		walletSettings.Save();

		return _wallets.First(x => x.Name == walletSettings.WalletName);
	}
}
