using ReactiveUI;
using System.Reactive.Linq;
using System.Threading.Tasks;
using WalletWasabi.Fluent.Models.UI;
using WalletWasabi.Fluent.ViewModels.Dialogs.Base;

namespace WalletWasabi.Fluent.ViewModels.Navigation;

public class NavigationState : ReactiveObject, INavigate
{
	public NavigationState(
		UiContext uiContext,
		INavigationStack<RoutableViewModel> homeScreenNavigation,
		INavigationStack<RoutableViewModel> dialogScreenNavigation,
		INavigationStack<RoutableViewModel> fullScreenNavigation,
		INavigationStack<RoutableViewModel> compactDialogScreenNavigation)
	{
		UiContext = uiContext;
		HomeScreen = homeScreenNavigation;
		DialogScreen = dialogScreenNavigation;
		FullScreen = fullScreenNavigation;
		CompactDialogScreen = compactDialogScreenNavigation;

		this.WhenAnyValue(
				x => x.DialogScreen.CurrentPage,
				x => x.CompactDialogScreen.CurrentPage,
				x => x.FullScreen.CurrentPage,
				x => x.HomeScreen.CurrentPage,
				(dialog, compactDialog, fullScreenDialog, mainScreen) => compactDialog ?? dialog ?? fullScreenDialog ?? mainScreen)
			.WhereNotNull()
			.ObserveOn(RxApp.MainThreadScheduler)
			.Do(OnCurrentPageChanged)
			.Subscribe();
	}

	public UiContext UiContext { get; }

	public INavigationStack<RoutableViewModel> HomeScreen { get; }

	public INavigationStack<RoutableViewModel> DialogScreen { get; }

	public INavigationStack<RoutableViewModel> FullScreen { get; }

	public INavigationStack<RoutableViewModel> CompactDialogScreen { get; }

	public INavigationStack<RoutableViewModel> Navigate(NavigationTarget currentTarget)
	{
		return currentTarget switch
		{
			NavigationTarget.HomeScreen => HomeScreen,
			NavigationTarget.DialogScreen => DialogScreen,
			NavigationTarget.FullScreen => FullScreen,
			NavigationTarget.CompactDialogScreen => CompactDialogScreen,
			_ => throw new NotSupportedException(),
		};
	}

	public FluentNavigate To()
	{
		return new FluentNavigate(UiContext);
	}

	private void OnCurrentPageChanged(RoutableViewModel page)
	{
		if (HomeScreen.CurrentPage is { } homeScreen)
		{
			homeScreen.IsActive = false;
		}

		if (DialogScreen.CurrentPage is { } dialogScreen)
		{
			dialogScreen.IsActive = false;
		}

		if (FullScreen.CurrentPage is { } fullScreen)
		{
			fullScreen.IsActive = false;
		}

		if (CompactDialogScreen.CurrentPage is { } compactDialogScreen)
		{
			compactDialogScreen.IsActive = false;
		}

		page.IsActive = true;
	}

	public async Task<DialogResult<TResult>> NavigateDialogAsync<TResult>(DialogViewModelBase<TResult> dialog, NavigationTarget target = NavigationTarget.Default, NavigationMode navigationMode = NavigationMode.Normal)
	{
		target = NavigationExtensions.GetTarget(dialog, target);
		return await Navigate(target).NavigateDialogAsync(dialog, navigationMode);
	}
}
