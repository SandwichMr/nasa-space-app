using System.Collections.Generic;
using Trainer.Application.Outpost;
namespace Trainer.Unity.Configuration
{
    /// <summary>Authoring plan shared by the installer and visual projection. Metres, not simulation state.</summary>
    public static class LunarBasePlan
    {
        public static BaseRect Habitat => new BaseRect(0,0,68.4,40);
        public static BaseRect Chamber => new BaseRect(68.4,17,74.4,23);
        public static BaseRect InnerHatch => new BaseRect(68,18.8,68.4,21.2);
        public static BaseRect OuterHatch => new BaseRect(74.4,18.8,74.8,21.2);
        public static BasePosition Entry => new BasePosition(75.4,20);
        public sealed class Room
        {
            public string Name {get;} public BaseRect Area {get;}
            public Room(string name,BaseRect area){Name=name;Area=area;}
        }
        public static IReadOnlyList<Room> Rooms {get;}=new[]{
            new Room("RECICLAJE DE AGUA",new BaseRect(.6,24.3,19.7,39.4)),
            new Room("SOPORTE DE AIRE",new BaseRect(20.3,24.3,39.7,39.4)),
            new Room("LABORATORIO",new BaseRect(40.3,24.3,67.8,39.4)),
            new Room("ALMACÉN Y LOGÍSTICA",new BaseRect(.6,.6,23.7,15.7)),
            new Room("ÁREA COMÚN",new BaseRect(24.3,.6,43.7,15.7)),
            new Room("REFUGIO",new BaseRect(44.3,.6,67.8,15.7)),
            new Room("PREPARACIÓN EVA",new BaseRect(56.3,16.3,67.8,23.7))};
        public static IReadOnlyList<BaseRect> Furniture {get;}=new[]{
            new BaseRect(31,7,37,9),new BaseRect(30,5,32,6),new BaseRect(36,5,38,6),
            new BaseRect(3,3,7,5),new BaseRect(13,3,17,5),new BaseRect(45,27,51,29),new BaseRect(57,27,63,29)};
        public static List<BaseRect> Obstacles()
        {
            var r=new List<BaseRect>{
                new BaseRect(0,0,.6,40),new BaseRect(0,0,68.4,.6),new BaseRect(0,39.4,68.4,40),
                new BaseRect(67.8,0,68.4,18.8),new BaseRect(67.8,21.2,68.4,40),
                new BaseRect(.6,23.7,8,24.3),new BaseRect(12,23.7,28,24.3),
                new BaseRect(32,23.7,49,24.3),new BaseRect(53,23.7,67.8,24.3),
                new BaseRect(19.7,24.3,20.3,39.4),new BaseRect(39.7,24.3,40.3,39.4),
                new BaseRect(.6,15.7,10,16.3),new BaseRect(14,15.7,32,16.3),
                new BaseRect(36,15.7,54,16.3),new BaseRect(58,15.7,67.8,16.3),
                new BaseRect(23.7,.6,24.3,15.7),new BaseRect(43.7,.6,44.3,15.7),
                new BaseRect(55.7,16.3,56.3,18),new BaseRect(55.7,22,56.3,23.7),
                new BaseRect(68.4,16.6,74.8,17),new BaseRect(68.4,23,74.8,23.4),
                new BaseRect(74.4,17,74.8,18.8),new BaseRect(74.4,21.2,74.8,23.4),
                new BaseRect(84,28,87,31),new BaseRect(91,10,94,13),new BaseRect(102,36,105,39)};
            r.AddRange(Furniture);return r;
        }
        public static BasePosition Station(string id)
        {
            switch(id)
            {
                case "water-recycler":return new BasePosition(7,35);
                case "air-scrubber":return new BasePosition(29,35);
                case "science-lab":return new BasePosition(49,34);
                case "sample-analyzer":return new BasePosition(61,34);
                case "suit-rack-a":return new BasePosition(57,21);
                case "suit-rack-b":return new BasePosition(60,21);
                case "suit-charger":return new BasePosition(63,21);
                case "consumables-panel":return new BasePosition(66,21);
                case "suit-check":return new BasePosition(64,18);
                case "shelter-panel":return new BasePosition(59,9);
                case "airlock-inner-panel":return new BasePosition(68.2,20);
                case "airlock-outer-panel":return new BasePosition(74.6,20);
                case "regolith-site":return new BasePosition(85,20);
                case "crater-rim":return new BasePosition(103,29);
                default:throw new System.ArgumentException("Unknown lunar station "+id);
            }
        }
    }
}
