using System;
using System.Collections.Generic;
using System.Linq;

namespace BotMain
{
    internal sealed class MatchEntityProvenanceRegistry
    {
        private readonly object _sync = new object();
        private readonly Dictionary<int, TrackedEntityState> _statesByEntityId = new Dictionary<int, TrackedEntityState>();
        private readonly List<PendingAcquisitionContext> _pendingAcquisitions = new List<PendingAcquisitionContext>();

        public void Reset()
        {
            lock (_sync)
            {
                _statesByEntityId.Clear();
                _pendingAcquisitions.Clear();
            }
        }

        public void ArmPendingAcquisition(PendingAcquisitionContext pending)
        {
            if (pending == null)
                return;

            lock (_sync)
            {
                PruneExpiredPending_NoLock();
                _pendingAcquisitions.Add(ClonePending(pending));
            }
        }

        public void Refresh(IReadOnlyList<EntityContextSnapshot> entities, int currentTurn)
        {
            if (entities == null)
                return;

            lock (_sync)
            {
                PruneExpiredPending_NoLock();
                foreach (var entity in entities.Where(entity => entity != null && entity.EntityId > 0))
                {
                    if (!_statesByEntityId.TryGetValue(entity.EntityId, out var tracked))
                    {
                        tracked = new TrackedEntityState
                        {
                            EntityId = entity.EntityId
                        };
                        _statesByEntityId[entity.EntityId] = tracked;
                    }

                    var previousCardId = tracked.CardId ?? string.Empty;
                    var cardChanged = !string.IsNullOrWhiteSpace(previousCardId)
                        && !string.Equals(previousCardId, entity.CardId ?? string.Empty, StringComparison.OrdinalIgnoreCase);

                    tracked.CardId = entity.CardId ?? string.Empty;
                    tracked.Zone = entity.Zone ?? string.Empty;
                    tracked.ZonePosition = entity.ZonePosition;
                    tracked.CreatorEntityId = entity.CreatorEntityId;
                    tracked.IsGenerated = entity.IsGenerated;
                    tracked.LastSeenTurn = currentTurn;

                    if (cardChanged)
                    {
                        tracked.Provenance = new CardProvenance
                        {
                            OriginKind = CardOriginKind.Transformed,
                            SourceEntityId = entity.EntityId,
                            SourceCardId = previousCardId,
                            AcquireTurn = currentTurn,
                            ChoiceMode = tracked.Provenance?.ChoiceMode ?? string.Empty
                        };
                    }
                    else if (tracked.Provenance == null || tracked.Provenance.OriginKind == CardOriginKind.Unknown)
                    {
                        tracked.Provenance = DetermineProvenance_NoLock(entity, currentTurn);
                    }

                    entity.Provenance = CloneProvenance(tracked.Provenance);
                }
            }
        }

        public CardProvenance ResolveProvenance(int entityId, string cardId = null)
        {
            lock (_sync)
            {
                if (entityId > 0 && _statesByEntityId.TryGetValue(entityId, out var tracked) && tracked.Provenance != null)
                    return CloneProvenance(tracked.Provenance);

                if (!string.IsNullOrWhiteSpace(cardId))
                {
                    var byCard = _statesByEntityId.Values
                        .Where(state => string.Equals(state.CardId, cardId, StringComparison.OrdinalIgnoreCase) && state.Provenance != null)
                        .OrderByDescending(state => state.LastSeenTurn)
                        .FirstOrDefault();
                    if (byCard?.Provenance != null)
                        return CloneProvenance(byCard.Provenance);
                }

                return new CardProvenance();
            }
        }

        private CardProvenance DetermineProvenance_NoLock(EntityContextSnapshot entity, int currentTurn)
        {
            if (entity == null)
                return new CardProvenance();

            if (TryConsumePending_NoLock(entity, out var pending))
            {
                pending.AcquireTurn = currentTurn;
                pending.SourceCardId = pending.SourceCardId ?? string.Empty;
                return pending;
            }

            if (entity.CreatorEntityId > 0)
            {
                _statesByEntityId.TryGetValue(entity.CreatorEntityId, out var creatorState);
                var creatorCardId = creatorState?.CardId ?? string.Empty;
                var originKind = !string.IsNullOrWhiteSpace(creatorCardId)
                    && string.Equals(creatorCardId, entity.CardId ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                    ? CardOriginKind.Copied
                    : CardOriginKind.Generated;

                return new CardProvenance
                {
                    OriginKind = originKind,
                    SourceEntityId = entity.CreatorEntityId,
                    SourceCardId = creatorCardId,
                    AcquireTurn = currentTurn
                };
            }

            if (string.Equals(entity.Zone, "HAND", StringComparison.OrdinalIgnoreCase))
            {
                return new CardProvenance
                {
                    OriginKind = currentTurn <= 1 ? CardOriginKind.StartingHand : CardOriginKind.DeckDrawn,
                    AcquireTurn = currentTurn
                };
            }

            return new CardProvenance
            {
                OriginKind = CardOriginKind.Unknown,
                AcquireTurn = currentTurn
            };
        }

        private bool TryConsumePending_NoLock(EntityContextSnapshot entity, out CardProvenance provenance)
        {
            provenance = null;
            if (entity == null || !string.Equals(entity.Zone, "HAND", StringComparison.OrdinalIgnoreCase))
                return false;

            for (var i = 0; i < _pendingAcquisitions.Count; i++)
            {
                var pending = _pendingAcquisitions[i];
                if (pending == null)
                    continue;

                var expected = pending.ExpectedCardIds ?? Array.Empty<string>();
                if (expected.Count > 0
                    && !expected.Any(cardId => string.Equals(cardId, entity.CardId ?? string.Empty, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                provenance = new CardProvenance
                {
                    OriginKind = pending.OriginKind,
                    SourceEntityId = pending.SourceEntityId,
                    SourceCardId = pending.SourceCardId ?? string.Empty,
                    AcquireTurn = pending.AcquireTurn,
                    ChoiceMode = pending.ChoiceMode ?? string.Empty
                };
                _pendingAcquisitions.RemoveAt(i);
                return true;
            }

            return false;
        }

        private void PruneExpiredPending_NoLock()
        {
            if (_pendingAcquisitions.Count == 0)
                return;

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _pendingAcquisitions.RemoveAll(pending =>
                pending == null
                || (pending.CreatedAtMs > 0 && pending.CreatedAtMs + 45000 < nowMs)
                || pending.ExpectedCardIds == null);
        }

        private static PendingAcquisitionContext ClonePending(PendingAcquisitionContext pending)
        {
            if (pending == null)
                return null;

            return new PendingAcquisitionContext
            {
                OriginKind = pending.OriginKind,
                SourceEntityId = pending.SourceEntityId,
                SourceCardId = pending.SourceCardId ?? string.Empty,
                AcquireTurn = pending.AcquireTurn,
                ChoiceMode = pending.ChoiceMode ?? string.Empty,
                ChoiceId = pending.ChoiceId,
                CreatedAtMs = pending.CreatedAtMs,
                ExpectedCardIds = pending.ExpectedCardIds?.ToArray() ?? Array.Empty<string>()
            };
        }

        private static CardProvenance CloneProvenance(CardProvenance provenance)
        {
            if (provenance == null)
                return new CardProvenance();

            return new CardProvenance
            {
                OriginKind = provenance.OriginKind,
                SourceEntityId = provenance.SourceEntityId,
                SourceCardId = provenance.SourceCardId ?? string.Empty,
                AcquireTurn = provenance.AcquireTurn,
                ChoiceMode = provenance.ChoiceMode ?? string.Empty
            };
        }

        private sealed class TrackedEntityState
        {
            public int EntityId { get; set; }
            public string CardId { get; set; } = string.Empty;
            public string Zone { get; set; } = string.Empty;
            public int ZonePosition { get; set; }
            public int CreatorEntityId { get; set; }
            public bool IsGenerated { get; set; }
            public int LastSeenTurn { get; set; }
            public CardProvenance Provenance { get; set; }
        }
    }
}
