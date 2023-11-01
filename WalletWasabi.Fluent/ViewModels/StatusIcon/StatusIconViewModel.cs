using System.Linq;
using System.Reactive.Linq;
using System.Windows.Input;
using ReactiveUI;
using WalletWasabi.Fluent.Extensions;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.Models.UI;
using WalletWasabi.Fluent.Models.Wallets;
using WalletWasabi.Helpers;

namespace WalletWasabi.Fluent.ViewModels.StatusIcon;

public partial class StatusIconViewModel : ViewModelBase
{
	[AutoNotify] private bool _isAskMeLaterVisible;
	[AutoNotify] private string? _versionText;

	public StatusIconViewModel(UiContext uiContext)
	{
		UiContext = uiContext;
		HealthMonitor = uiContext.HealthMonitor;

		ManualUpdateCommand = ReactiveCommand.CreateFromTask(() => UiContext.FileSystem.OpenBrowserAsync("https://wasabiwallet.io/#download"));
		UpdateCommand = ReactiveCommand.Create(() =>
		{
			UiContext.ApplicationSettings.DoUpdateOnClose = true;
			AppLifetimeHelper.Shutdown();
		});

		IsAskMeLaterVisible = true;

		AskMeLaterCommand = ReactiveCommand.Create(() => IsAskMeLaterVisible = false, this.WhenAnyValue(x => x.HealthMonitor.UpdateAvailable, x => x.IsAskMeLaterVisible, (a, b) => a && b));

		OpenTorStatusSiteCommand = ReactiveCommand.CreateFromTask(() => UiContext.FileSystem.OpenBrowserAsync("https://status.torproject.org"));

		HealthMonitor.WhenAnyValue(x => x.CriticalUpdateAvailable, x => x.IsReadyToInstall, x => x.UpdateAvailable)
					 .Select(_ => GetVersionText())
					 .BindTo(this, x => x.VersionText);
	}

	public IHealthMonitor HealthMonitor { get; }

	public ICommand OpenTorStatusSiteCommand { get; }

	public ICommand UpdateCommand { get; }
	public ICommand ManualUpdateCommand { get; }

	public ICommand AskMeLaterCommand { get; }

	public string BitcoinCoreName => Constants.BuiltinBitcoinNodeName;

	private string GetVersionText()
	{
		if (HealthMonitor.CriticalUpdateAvailable)
		{
			return $"Critical update required";
		}
		else if (HealthMonitor.IsReadyToInstall)
		{
			return $"Version {HealthMonitor.ClientVersion} is now ready to install";
		}
		else if (HealthMonitor.UpdateAvailable)
		{
			return $"Version {HealthMonitor.ClientVersion} is now available";
		}

		return string.Empty;
	}
}
