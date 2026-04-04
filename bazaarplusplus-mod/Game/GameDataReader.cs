#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Players;
using BazaarPlusPlus.Core.Runtime;
using TheBazaar;

namespace BazaarPlusPlus;

internal static class GameDataReader
{
    public static RunInfo GetRunInfo()
    {
        if (Data.Run == null)
        {
            BppLog.Warn("GameDataReader", "GetRunInfo requested while Data.Run is null");
            return new RunInfo
            {
                Name = BppClientCacheBridge.TryGetProfileUsername(),
                AvailableEncounters = new List<RunInfo.CardInfo>(),
                CurrentEncounterChoices = new List<RunInfo.CardInfo>(),
            };
        }

        var opponent = Data.Run.Opponent;
        BppLog.Debug(
            "GameDataReader",
            $"Building run snapshot: hero={Data.Run.Player?.Hero}, day={Data.Run.Day}, opponent={(opponent == null ? "none" : opponent.Hero.ToString())}"
        );

        return new RunInfo
        {
            Wins = Data.Run.Victories,
            Losses = Data.Run.Losses,
            Hero = Data.Run.Player.Hero.ToString(),
            Day = (int)Data.Run.Day,
            Gold = Data.Run.Player.GetAttributeValue(EPlayerAttributeType.Gold),
            Income = Data.Run.Player.GetAttributeValue(EPlayerAttributeType.Income),
            Cards = GetCardInfo(GetItemsAsCards(Data.Run.Player.Hand)),
            Stash = GetCardInfo(GetItemsAsCards(Data.Run.Player.Stash)),
            Skills = GetSkillInfo(Data.Run.Player.Skills),
            OppCards = GetCardInfo(GetItemsAsCards(Data.Run.Opponent?.Hand)),
            OppStash = GetCardInfo(GetItemsAsCards(Data.Run.Opponent?.Stash)),
            OppSkills = GetSkillInfo(Data.Run.Opponent?.Skills),
            Health = Data.Run.Player.GetAttributeValue(EPlayerAttributeType.HealthMax),
            Shield = Data.Run.Player.GetAttributeValue(EPlayerAttributeType.Shield),
            Regen = Data.Run.Player.GetAttributeValue(EPlayerAttributeType.HealthRegen),
            Level = Data.Run.Player.GetAttributeValue(EPlayerAttributeType.Level),
            Prestige = Data.Run.Player.GetAttributeValue(EPlayerAttributeType.Prestige),
            Name = BppClientCacheBridge.TryGetProfileUsername(),
            OppHealth = Data.Run.Opponent?.GetAttributeValue(EPlayerAttributeType.HealthMax),
            OppRegen = Data.Run.Opponent?.GetAttributeValue(EPlayerAttributeType.HealthRegen),
            OppName = Data.Run.Opponent?.Hero == EHero.Common ? "PvE" : Data.SimPvpOpponent?.Name,
            OppHero = Data.Run.Opponent?.Hero.ToString(),
            OppShield = Data.Run.Opponent?.GetAttributeValue(EPlayerAttributeType.Shield),
            OppGold = Data.Run.Opponent?.GetAttributeValue(EPlayerAttributeType.Gold),
            OppIncome = Data.Run.Opponent?.GetAttributeValue(EPlayerAttributeType.Income),
            OppLevel = Data.Run.Opponent?.GetAttributeValue(EPlayerAttributeType.Level),
            OppPrestige = Data.Run.Opponent?.GetAttributeValue(EPlayerAttributeType.Prestige),
            PlayMode = Data.SelectedPlayMode == EPlayMode.Ranked,
            AvailableEncounters = new List<RunInfo.CardInfo>(),
            CurrentEncounterChoices = new List<RunInfo.CardInfo>(),
        };
    }

    public static List<Card> GetItemsAsCards(IPlayerInventory container)
    {
        if (container?.Container == null)
        {
            BppLog.Debug(
                "GameDataReader",
                "Inventory container missing, returning empty card list"
            );
            return new List<Card>();
        }

        return container.Container.GetSocketables().Cast<Card>().ToList();
    }

    public static List<RunInfo.SkillInfo> GetSkillInfo(IEnumerable<SkillCard> skills)
    {
        var skillInfos = new List<RunInfo.SkillInfo>();
        if (skills == null)
        {
            BppLog.Debug("GameDataReader", "Skill collection missing, returning empty skill list");
            return skillInfos;
        }

        foreach (var skill in skills)
        {
            if (skill.Template != null)
                skillInfos.Add(
                    new RunInfo.SkillInfo
                    {
                        TemplateId = skill.TemplateId,
                        Tier = skill.Tier,
                        Name = skill.Template.Localization.Title.Text,
                        Attributes = skill.Attributes,
                    }
                );
        }
        return skillInfos;
    }

    public static List<RunInfo.CardInfo> GetCardInfo(List<Card> cards)
    {
        var cardInfos = new List<RunInfo.CardInfo>();
        if (cards == null || cards.Count == 0)
            return cardInfos;

        foreach (var card in cards)
        {
            cardInfos.Add(
                new RunInfo.CardInfo
                {
                    TemplateId = card.TemplateId,
                    Tier = card.Tier,
                    Left = card.LeftSocketId,
                    Instance = card.GetInstanceId(),
                    Attributes = card.Attributes,
                    Tags = card.Tags,
                    Name = card.Template?.InternalName,
                    Enchant = card.GetEnchantment().ToString(),
                }
            );
        }
        return cardInfos;
    }
}
