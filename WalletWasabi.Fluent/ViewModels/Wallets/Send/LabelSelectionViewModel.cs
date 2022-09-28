using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using NBitcoin;
using ReactiveUI;
using WalletWasabi.Blockchain.Analysis.Clustering;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Fluent.Extensions;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.Models;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Send;

public partial class LabelSelectionViewModel : ViewModelBase
{
	private readonly Money _targetAmount;
	private readonly FeeRate _feeRate;
	private readonly SmartLabel _recipient;
	private readonly List<Pocket> _hiddenIncludedPockets = new();

	[AutoNotify] private bool _enoughSelected;

	private Pocket _privatePocket = Pocket.Empty;
	private Pocket _semiPrivatePocket = Pocket.Empty;
	private Pocket[] _allPockets = Array.Empty<Pocket>();

	public LabelSelectionViewModel(Money targetAmount, FeeRate feeRate, SmartLabel recipient)
	{
		_targetAmount = targetAmount;
		_feeRate = feeRate;
		_recipient = recipient;
	}

	public Pocket[] NonPrivatePockets { get; set; } = Array.Empty<Pocket>();

	public IEnumerable<LabelViewModel> AllLabelsViewModel { get; set; } = Array.Empty<LabelViewModel>();

	public IEnumerable<LabelViewModel> LabelsWhiteList => AllLabelsViewModel.Where(x => !x.IsBlackListed);

	public IEnumerable<LabelViewModel> LabelsBlackList => AllLabelsViewModel.Where(x => x.IsBlackListed);

	public Pocket[] AutoSelectPockets()
	{
		var privateAndSemiPrivatePockets = new[] { _privatePocket, _semiPrivatePocket };

		var knownPockets = NonPrivatePockets.Where(x => x.Labels != CoinPocketHelper.UnlabelledFundsText).ToArray();
		var unknownPockets = NonPrivatePockets.Except(knownPockets).ToArray();

		var privateAndSemiPrivateAndUnknownPockets = privateAndSemiPrivatePockets.Union(unknownPockets).ToArray();
		var privateAndSemiPrivateAndKnownPockets = privateAndSemiPrivatePockets.Union(knownPockets).ToArray();

		var knownByRecipientPockets = knownPockets.Where(pocket => pocket.Labels.Any(label => _recipient.Contains(label, StringComparer.OrdinalIgnoreCase))).ToArray();
		var onlyKnownByRecipientPockets = knownByRecipientPockets.Where(pocket => pocket.Labels.Equals(_recipient, StringComparer.OrdinalIgnoreCase)).ToArray();

		if (onlyKnownByRecipientPockets.EffectiveSumValue(_feeRate) >= _targetAmount)
		{
			return onlyKnownByRecipientPockets;
		}

		if (_privatePocket.EffectiveSumValue(_feeRate) >= _targetAmount)
		{
			return new[] { _privatePocket };
		}

		if (privateAndSemiPrivatePockets.EffectiveSumValue(_feeRate) >= _targetAmount)
		{
			return privateAndSemiPrivatePockets;
		}

		if (TryGetBestKnownByRecipientPockets(knownByRecipientPockets, _targetAmount, _feeRate, _recipient, out var pockets))
		{
			return pockets;
		}

		if (privateAndSemiPrivateAndKnownPockets.EffectiveSumValue(_feeRate) >= _targetAmount)
		{
			return privateAndSemiPrivateAndKnownPockets;
		}

		if (privateAndSemiPrivateAndUnknownPockets.EffectiveSumValue(_feeRate) >= _targetAmount)
		{
			return privateAndSemiPrivateAndUnknownPockets;
		}

		return _allPockets.ToArray();
	}

	private bool TryGetBestKnownByRecipientPockets(Pocket[] knownByRecipientPockets, Money targetAmount, FeeRate feeRate, SmartLabel recipient, [NotNullWhen(true)] out Pocket[]? pockets)
	{
		pockets = null;

		if (knownByRecipientPockets.EffectiveSumValue(feeRate) < _targetAmount)
		{
			return false;
		}

		var privacyRankedPockets =
			knownByRecipientPockets
				.Select(pocket =>
				{
					var containedRecipientLabelsCount = pocket.Labels.Count(label => recipient.Contains(label, StringComparer.OrdinalIgnoreCase));
					var totalPocketLabelsCount = pocket.Labels.Count();
					var totalRecipientLabelsCount = recipient.Count();
					var index = ((double)containedRecipientLabelsCount / totalPocketLabelsCount) + ((double)containedRecipientLabelsCount / totalRecipientLabelsCount);

					return (acceptabilityIndex: index, pocket);
				})
				.OrderByDescending(tup => tup.acceptabilityIndex)
				.ThenBy(tup => tup.pocket.Labels.Count())
				.ThenByDescending(tup => tup.pocket.Amount)
				.Select(tup => tup.pocket)
				.ToArray();

		var bestPockets = new List<Pocket>();
		foreach (var p in privacyRankedPockets)
		{
			bestPockets.Add(p);

			if (bestPockets.EffectiveSumValue(feeRate) >= targetAmount)
			{
				break;
			}
		}

		foreach (var p in bestPockets.OrderBy(x => x.Amount).ThenByDescending(x => x.Labels.Count()).ToImmutableArray())
		{
			if (bestPockets.EffectiveSumValue(feeRate) - p.EffectiveSumValue(feeRate) >= targetAmount)
			{
				bestPockets.Remove(p);
			}
			else
			{
				break;
			}
		}

		pockets = bestPockets.ToArray();
		return true;
	}

	public Pocket[] GetUsedPockets() =>
		NonPrivatePockets.Where(x => x.Labels.All(label => LabelsWhiteList.Any(labelViewModel => labelViewModel.Value == label)))
			.Union(_hiddenIncludedPockets)
			.ToArray();

	public void Reset(Pocket[] pockets)
	{
		_allPockets = pockets;

		if (pockets.FirstOrDefault(x => x.Labels == CoinPocketHelper.PrivateFundsText) is { } privatePocket)
		{
			_privatePocket = privatePocket;
		}

		if (pockets.FirstOrDefault(x => x.Labels == CoinPocketHelper.SemiPrivateFundsText) is { } semiPrivatePocket)
		{
			_semiPrivatePocket = semiPrivatePocket;
		}

		NonPrivatePockets = pockets.Where(x => x != _privatePocket && x != _semiPrivatePocket).ToArray();

		var allLabels = SmartLabel.Merge(NonPrivatePockets.Select(x => x.Labels));
		AllLabelsViewModel = allLabels.Select(x => new LabelViewModel(this, x)).ToArray();

		if (AllLabelsViewModel.FirstOrDefault(x => x.Value == CoinPocketHelper.UnlabelledFundsText) is { } unlabelledViewModel)
		{
			unlabelledViewModel.IsDangerous = true;
			unlabelledViewModel.ToolTip = "There is no information about these people, only use it when necessary!";
		}

		OnSelectionChanged();
	}

	public LabelViewModel[] GetAssociatedLabels(LabelViewModel labelViewModel)
	{
		if (labelViewModel.IsBlackListed)
		{
			var associatedPocketLabels = NonPrivatePockets.OrderBy(x => x.Labels.Count()).First(x => x.Labels.Contains(labelViewModel.Value)).Labels;
			return LabelsBlackList.Where(x => associatedPocketLabels.Contains(x.Value)).ToArray();
		}
		else
		{
			var associatedPockets = NonPrivatePockets.Where(x => x.Labels.Contains(labelViewModel.Value));
			var notAssociatedPockets = NonPrivatePockets.Except(associatedPockets);
			var allNotAssociatedLabels = SmartLabel.Merge(notAssociatedPockets.Select(x => x.Labels));
			return LabelsWhiteList.Where(x => !allNotAssociatedLabels.Contains(x.Value)).ToArray();
		}
	}

	public void OnFade(LabelViewModel source)
	{
		foreach (var lvm in source.IsBlackListed ? LabelsBlackList : LabelsWhiteList)
		{
			if (!lvm.IsHighlighted)
			{
				lvm.Fade(source);
			}
		}
	}

	public void OnPointerOver(LabelViewModel labelViewModel)
	{
		var affectedLabelViewModels = GetAssociatedLabels(labelViewModel);

		foreach (var lvm in affectedLabelViewModels)
		{
			lvm.Highlight(triggerSource: labelViewModel);
		}
	}

	public void SwapLabel(LabelViewModel labelViewModel)
	{
		var affectedLabelViewModels = GetAssociatedLabels(labelViewModel);

		foreach (var lvm in affectedLabelViewModels)
		{
			lvm.Swap();
		}

		OnSelectionChanged();
	}

	private void OnSelectionChanged()
	{
		_hiddenIncludedPockets.Clear();

		var (isPrivateNeeded, isSemiPrivateNeeded) = IsPrivateAndSemiPrivatePocketNeeded();

		if (isPrivateNeeded)
		{
			_hiddenIncludedPockets.Add(_privatePocket);
		}

		if (isSemiPrivateNeeded)
		{
			_hiddenIncludedPockets.Add(_semiPrivatePocket);
		}

		EnoughSelected = GetUsedPockets().EffectiveSumValue(_feeRate) >= _targetAmount;

		this.RaisePropertyChanged(nameof(LabelsWhiteList));
		this.RaisePropertyChanged(nameof(LabelsBlackList));
	}

	private (bool, bool) IsPrivateAndSemiPrivatePocketNeeded()
	{
		var isPrivateNeeded = false;
		var isSemiPrivateNeeded = false;

		var usedPockets = GetUsedPockets();
		var usedPocketsLabels = new SmartLabel(usedPockets.SelectMany(p => p.Labels));

		if (usedPocketsLabels != _recipient || usedPockets.EffectiveSumValue(_feeRate) < _targetAmount)
		{
			isPrivateNeeded = true;

			if (Pocket.Merge(usedPockets, _privatePocket).EffectiveSumValue(_feeRate) < _targetAmount)
			{
				isSemiPrivateNeeded = true;
			}
		}

		return (isPrivateNeeded, isSemiPrivateNeeded);
	}

	public void SetUsedLabel(IEnumerable<SmartCoin>? usedCoins, int privateThreshold)
	{
		if (usedCoins is null)
		{
			return;
		}

		usedCoins = usedCoins.ToImmutableArray();

		var usedLabels = SmartLabel.Merge(usedCoins.Select(x => x.GetLabels(privateThreshold)));
		var usedLabelViewModels = AllLabelsViewModel.Where(x => usedLabels.Contains(x.Value, StringComparer.OrdinalIgnoreCase)).ToArray();
		var notUsedLabelViewModels = AllLabelsViewModel.Except(usedLabelViewModels);

		foreach (LabelViewModel label in notUsedLabelViewModels)
		{
			label.Swap();
		}

		OnSelectionChanged();
	}

	public bool IsOtherSelectionPossible(IEnumerable<SmartCoin> usedCoins, SmartLabel recipient)
	{
		var usedPockets = _allPockets.Where(pocket => pocket.Coins.Any(usedCoins.Contains)).ToImmutableArray();
		var remainingUsablePockets = _allPockets.Except(usedPockets).ToList();

		if (!usedPockets.Contains(_privatePocket)) // Private pocket hasn't been used. Don't deal with it then.
		{
			remainingUsablePockets.Remove(_privatePocket);
		}

		if (!usedPockets.Contains(_semiPrivatePocket)) // Semi private pocket hasn't been used. Don't deal with it then.
		{
			remainingUsablePockets.Remove(_semiPrivatePocket);
		}

		if (usedPockets.Length == 1 && usedPockets.First() == _privatePocket)
		{
			return false;
		}

		if (usedPockets.Length == 1 && usedPockets.First() == _semiPrivatePocket)
		{
			return false;
		}

		if (usedPockets.Length == 2 && usedPockets.Contains(_privatePocket) && usedPockets.Contains(_semiPrivatePocket))
		{
			return false;
		}

		if (!remainingUsablePockets.Any())
		{
			return false;
		}

		var labels = SmartLabel.Merge(usedPockets.Select(x => x.Labels));
		if (labels.Equals(recipient, StringComparer.OrdinalIgnoreCase))
		{
			return false;
		}

		return true;
	}
}
