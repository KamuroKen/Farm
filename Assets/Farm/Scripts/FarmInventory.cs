using System;
using System.Collections.Generic;
using UnityEngine;

namespace Farm
{
    public sealed class FarmItem
    {
        public readonly string Id, Name, Description;
        public readonly Sprite Icon;
        public FarmItem(string id,string name,string description,Sprite icon) {Id=id;Name=name;Description=description;Icon=icon;}
    }
    public sealed class FarmInventory
    {
        public const int Capacity=24, StackLimit=99;
        public struct Slot { public string Item; public int Count; }
        private Slot[] slots=new Slot[Capacity];
        private readonly Dictionary<string,FarmItem> items=new Dictionary<string,FarmItem>();
        public event Action Changed;
        public void Register(FarmItem item) => items.Add(item.Id,item);
        public FarmItem Item(string id) => id != null && items.TryGetValue(id,out var item) ? item : null;
        public Slot GetSlot(int index) => slots[index];
        public int Count(string id) {int total=0;foreach(var slot in slots) if(slot.Item==id) total+=slot.Count;return total;}
        private bool Pack(IReadOnlyDictionary<string,int> reward,out Slot[] result)
        {
            result=(Slot[])slots.Clone();
            foreach(var pair in reward)
            {
                if(!items.ContainsKey(pair.Key) || pair.Value<=0) return false;
                int left=pair.Value;
                for(int i=0;i<result.Length && left>0;i++)
                    if(result[i].Item==pair.Key) {int amount=Math.Min(left,StackLimit-result[i].Count);result[i].Count+=amount;left-=amount;}
                for(int i=0;i<result.Length && left>0;i++)
                    if(result[i].Count==0) {int amount=Math.Min(left,StackLimit);result[i]=new Slot{Item=pair.Key,Count=amount};left-=amount;}
                if(left>0) return false;
            }
            return true;
        }
        public bool CanAdd(IReadOnlyDictionary<string,int> reward) => Pack(reward,out _);
        public bool TryAdd(IReadOnlyDictionary<string,int> reward)
        {if(!Pack(reward,out var result)) return false;slots=result;Changed?.Invoke();return true;}
        public bool TryAdd(string item,int count) => TryAdd(new Dictionary<string,int>{{item,count}});
        public bool TryRemove(string item,int amount)
        {
            if(amount<=0 || Count(item)<amount) return false;
            for(int i=0;i<slots.Length && amount>0;i++) if(slots[i].Item==item)
            {int take=Math.Min(amount,slots[i].Count);slots[i].Count-=take;amount-=take;if(slots[i].Count==0) slots[i]=default;}
            Changed?.Invoke();return true;
        }
        public void Move(int from,int to)
        {
            if(from<0||to<0||from>=Capacity||to>=Capacity||from==to||slots[from].Count==0)return;
            if(slots[from].Item==slots[to].Item)
            {
                int amount=Math.Min(slots[from].Count,StackLimit-slots[to].Count);
                slots[to].Count+=amount;slots[from].Count-=amount;if(slots[from].Count==0)slots[from]=default;
            }
            else {var old=slots[to];slots[to]=slots[from];slots[from]=old;}
            Changed?.Invoke();
        }
    }
}
