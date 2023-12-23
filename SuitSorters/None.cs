using System;
using UnityEngine;

namespace MoreSuits.SuitSorters
{
    public class None : SuitSorter
    {
        public override void SortSuitRack(StartOfRound startOfRound, UnlockableSuit[] suits)
        {
            int index = 0;
            foreach (UnlockableSuit suit in suits)
            {
                AutoParentToShip component = suit.gameObject.GetComponent<AutoParentToShip>();
                component.overrideOffset = true;

                float offsetModifier = 0.18f;
                if (MoreSuitsMod.MakeSuitsFitOnRack && suits.Length > MoreSuitsMod.SUITS_PER_RACK)
                {
                    offsetModifier = offsetModifier / (Math.Min(suits.Length, 20) / 12f); // squish the suits together to make them all fit
                }

                component.positionOffset = new Vector3(-2.45f, 2.75f, -8.41f) + startOfRound.rightmostSuitPosition.forward * offsetModifier * (float)index;
                component.rotationOffset = new Vector3(0f, 90f, 0f);

                index++;
            }
        }
    }
}