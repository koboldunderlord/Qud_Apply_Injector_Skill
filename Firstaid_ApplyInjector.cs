using System;
using XRL.Language;
using XRL.Messages;
using XRL.UI;
using XRL.World.Parts.Effects;
using System.Collections.Generic;
using XRL.Rules;

namespace XRL.World.Parts.Skill
{
	[Serializable]
	internal class Firstaid_ApplyInjector : BaseSkill
	{
		public Guid ActivatedAbilityID = Guid.Empty;

		public Firstaid_ApplyInjector()
		{
			DisplayName = "Firstaid_ApplyInjector";
		}

		public override void Register(GameObject Object)
		{
			Object.RegisterPartEvent(this, "CommandApplyInjector");
			base.Register(Object);
		}

		public bool InjectorFilter(GameObject Item) 
		{
			return Item != null && Item.HasPart("Tonic");
		}

		public override bool FireEvent(Event E)
		{
			if (E.ID == "CommandApplyInjector")
			{
				// Exit if frozen
				if (!ParentObject.CheckFrozen())
				{
					if (ParentObject.IsPlayer())
					{
						Popup.Show("You are frozen!");
					}
					return false;
				}

				// get all injectors, or exit if none
				Body BodyObj = ParentObject.GetPart<Body>();
				List<GameObject> Injectors = BodyObj == null ? null : BodyObj.GetEquippedObjects(InjectorFilter);
				if (Injectors == null || Injectors.Count == 0) {
					if (ParentObject.IsPlayer())
					{
						Popup.Show("Equip at least one usable injector in a hand!");
					}
					return false;
				}

				// check if valid target
				Cell Cell = PickDirection();
				if (Cell != null)
				{
					// Get all valid targets, respecting phasing/flying/light
					List<GameObject> Targets = Cell.GetObjectsWithPart("Brain", ParentObject, ParentObject, ParentObject, true, true, true);
					// Remove hostile targets
					Targets.RemoveAll(T => T.pBrain.IsHostileTowards(ParentObject));

					if (Targets.Count == 0)
					{
						if (ParentObject.IsPlayer())
						{
							Popup.Show("No valid target!");
						}
						return false;
					}

					// We have a valid target!  Check if in combat.
					GameObject Target = Targets[0];
					foreach (GameObject Injector in Injectors)
					{
						
						if (Target.AreHostilesNearby()) // Make a attack against DV before injecting
						{
							// Lifted from Combat.cs
							int Roll = Stat.Random(1, 20);
							int BaseRoll = Roll;
							// we don't include movement bonuses in this roll since it's an ability activation
							Roll += ParentObject.GetIntProperty("HitBonus", 0);
							
							if (Injector != null)
							{
								Roll += Injector.GetIntProperty("HitBonus", 0);
							}
							
							int AgilityMod = ParentObject.StatMod("Agility");
							Roll += AgilityMod;
							Event RollEvent = Event.New("RollMeleeToHit");
							RollEvent.AddParameter("Weapon", Injector);
							RollEvent.AddParameter("Damage", 0);
							RollEvent.AddParameter("Defender", Target);
							RollEvent.AddParameter("Result", Roll);
							RollEvent.AddParameter("Skill", "ShortBlades");
							RollEvent.AddParameter("Stat", "Agility");
							Injector?.FireEvent(RollEvent);
							RollEvent.ID = "AttackerRollMeleeToHit";
							Injector?.FireEvent(RollEvent);
							Roll = RollEvent.GetIntParameter("Result");
							Event DVEvent = Event.New("GetDefenderDV");
							DVEvent.AddParameter("Weapon", Injector);
							DVEvent.AddParameter("Damage", 0);
							DVEvent.AddParameter("Defender", Target);
							DVEvent.AddParameter("NaturalHitResult", BaseRoll);
							DVEvent.AddParameter("Result", Roll);
							DVEvent.AddParameter("Skill", "ShortBlades");
							DVEvent.AddParameter("Stat", "Agility");
							DVEvent.AddParameter("DV", Stats.GetCombatDV(Target));
							Target.FireEvent(DVEvent);
							DVEvent.ID = "WeaponGetDefenderDV";
							Injector?.FireEvent(DVEvent);
							// for masterwork mod, plus natural attacker/defender bonuses/penalties
							int NaturalHitBonus = 0; 
							Event NaturalHitBonusEvent = Event.New("GetNaturalHitBonus", "Result", NaturalHitBonus);
							Injector.FireEvent(NaturalHitBonusEvent);
							
							NaturalHitBonusEvent.ID = "AttackerGetNaturalHitBonus";
							ParentObject.FireEvent(NaturalHitBonusEvent);
							NaturalHitBonusEvent.ID = "DefenderGetNaturalHitBonus";
							Target.FireEvent(NaturalHitBonusEvent);
							NaturalHitBonus = NaturalHitBonusEvent.GetIntParameter("Result");
							if (BaseRoll + NaturalHitBonus < 20 && Roll <= DVEvent.GetIntParameter("DV")) // no autohit
							{
								// Chance to fumble, dropping the injector
								string Color = "&r";
								
								int Diff = DVEvent.GetIntParameter("DV") - Roll;
								if (Stat.Random(1, 20) + AgilityMod <= Diff)
								{
									// fumble!!!
									if (ParentObject.IsPlayer()) 
									{
										IPart.AddPlayerMessage(Color + "You miss, dropping the " + Injector.DisplayName + "!");
									}
									ParentObject.FireEvent(Event.New("Unequipped", "UnequippingObject", Injector));
									Event UnequipEvent = Event.New("PerformDrop", "Object", Injector);
									UnequipEvent.bSilent = E.bSilent;
									ParentObject.FireEvent(UnequipEvent);
								} else 
								{
									if (ParentObject.IsPlayer()) {
										IPart.AddPlayerMessage(Color + "You miss with the " + Injector.DisplayName + "!");
									}
								}
								continue;
							} 
							
						} 

						IPart.AddPlayerMessage("firing injector");
						// no hostiles or didn't miss - fire the injector
						Injector.FireEvent(Event.New("InvCommandApply", "Owner", Target, "Attacker", ParentObject));
					}

					// Deal with energy cost - reduced by short blades skill
					int EnergyCost = 1000;
					if (ParentObject.HasSkill("ShortBlades_Expertise"))
					{
						EnergyCost = 750;
					}
					ParentObject.UseEnergy(EnergyCost, "Physical Skill");
					return true;
				}
			}
			return false;
		}

		public override bool AddSkill(GameObject GO)
		{
			ActivatedAbilities activatedAbilities = GO.GetPart("ActivatedAbilities") as ActivatedAbilities;
			if (activatedAbilities != null)
			{
				ActivatedAbilityID = activatedAbilities.AddAbility("Apply Injector", "CommandApplyInjector", "Skill", -1);
			}
			return true;
		}

		public override bool RemoveSkill(GameObject GO)
		{
			if (ActivatedAbilityID != Guid.Empty)
			{
				ActivatedAbilities activatedAbilities = GO.GetPart("ActivatedAbilities") as ActivatedAbilities;
				activatedAbilities.RemoveAbility(ActivatedAbilityID);
			}
			return true;
		}
	}
}
