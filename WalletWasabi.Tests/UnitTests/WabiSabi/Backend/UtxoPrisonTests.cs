using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WalletWasabi.Tests.Helpers;
using WalletWasabi.WabiSabi.Backend.Banning;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.WabiSabi.Backend
{
	public class UtxoPrisonTests
	{
		[Fact]
		public void EmptyPrison()
		{
			var p = new Prison(Enumerable.Empty<Inmate>());
			Assert.Empty(p.GetInmates());
			Assert.Equal(0, p.CountInmates().noted);
			Assert.Equal(0, p.CountInmates().banned);
			Assert.False(p.TryGet(BitcoinFactory.CreateOutPoint(), out _));
		}

		[Fact]
		public void PrisonChangeTracking()
		{
			var p = new Prison(Enumerable.Empty<Inmate>());
			var currentChangeId = p.ChangeId;

			// Make sure we set them to the past so the release method that looks at the time evaluates to true.
			var past = DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(2);

			p.Punish(new Inmate(BitcoinFactory.CreateOutPoint(), Punishment.Banned, past, 1));
			Assert.NotEqual(currentChangeId, p.ChangeId);
			currentChangeId = p.ChangeId;

			p.Punish(new Inmate(BitcoinFactory.CreateOutPoint(), Punishment.Noted, past, 1));
			Assert.NotEqual(currentChangeId, p.ChangeId);
			currentChangeId = p.ChangeId;

			var op = BitcoinFactory.CreateOutPoint();
			p.Punish(new Inmate(op, Punishment.Noted, past, 2));
			Assert.NotEqual(currentChangeId, p.ChangeId);
			currentChangeId = p.ChangeId;

			p.Punish(new Inmate(op, Punishment.Noted, past, 1));
			Assert.NotEqual(currentChangeId, p.ChangeId);
			currentChangeId = p.ChangeId;

			Assert.True(p.TryRelease(op, out _));
			Assert.NotEqual(currentChangeId, p.ChangeId);
			currentChangeId = p.ChangeId;

			p.ReleaseEligibleInmates(TimeSpan.FromMilliseconds(1));
			Assert.NotEqual(currentChangeId, p.ChangeId);
		}

		[Fact]
		public void PrisonOperations()
		{
			var p = new Prison(Enumerable.Empty<Inmate>());

			var utxo = BitcoinFactory.CreateOutPoint();
			p.Punish(utxo, Punishment.Noted, 1);
			Assert.Single(p.GetInmates());
			Assert.Equal(1, p.CountInmates().noted);
			Assert.Equal(0, p.CountInmates().banned);
			Assert.True(p.TryGet(utxo, out _));

			// Updates to banned.
			p.Punish(utxo, Punishment.Banned, 1);
			Assert.Single(p.GetInmates());
			Assert.Equal(0, p.CountInmates().noted);
			Assert.Equal(1, p.CountInmates().banned);
			Assert.True(p.TryGet(utxo, out _));

			// Removes.
			Assert.True(p.TryRelease(utxo, out _));
			Assert.Empty(p.GetInmates());

			// Noting twice flips to banned.
			p.Punish(utxo, Punishment.Noted, 1);
			p.Punish(utxo, Punishment.Noted, 1);
			Assert.Single(p.GetInmates());
			Assert.Equal(0, p.CountInmates().noted);
			Assert.Equal(1, p.CountInmates().banned);
			Assert.True(p.TryGet(utxo, out _));
			Assert.True(p.TryRelease(utxo, out _));

			// Updates round.
			p.Punish(utxo, Punishment.Banned, 1);
			p.Punish(utxo, Punishment.Banned, 2);
			Assert.Single(p.GetInmates());
			Assert.True(p.TryGet(utxo, out var inmate));
			Assert.Equal(2ul, inmate!.LastDisruptedRoundId);
			Assert.True(p.TryRelease(utxo, out _));
		}
	}
}
