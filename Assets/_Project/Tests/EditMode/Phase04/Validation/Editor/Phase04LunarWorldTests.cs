using System.Linq;
using NUnit.Framework;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Unity.Configuration;
using Trainer.Unity.Validation.Phase04;
using Trainer.Unity.World.Expedition;
using UnityEditor;
using UnityEngine;
namespace Trainer.Tests.Phase04.Validation
{
    public sealed class Phase04LunarWorldTests
    {
        [Test]
        public void NewHabitatKeepsEveryInteriorStationReachableAndExteriorExtendsPastTheOldEdge()
        {
            var definition=AssetDatabase.LoadAssetAtPath<BaseOperationsScenarioDefinition>("Assets/_Project/Data/Scenarios/Phase04/Phase04_LivingBase_Synthetic.asset");
            var s=definition.ToScenario();
            Assert.That(s.Expedition.Outpost.Movement.CrewRadiusMeters,Is.EqualTo(.35));
            Assert.That(LunarBasePlan.Habitat.Width*LunarBasePlan.Habitat.Height,Is.GreaterThan(2600),"actual habitat must be enlarged, not just zoomed out");
            Assert.That(s.Expedition.Airlock.Chamber.MinX,Is.GreaterThan(68));
            var navigator=new Phase04Navigator(s.Expedition);
            Assert.That(navigator.Plan(s.Layout.SpawnPoints[0],p=>p.DistanceTo(new BasePosition(34,12))<.5),Is.Not.Null,"common room must be reachable");
            foreach(var station in s.Layout.Stations.Where(x=>x.Position.X<LunarBasePlan.InnerHatch.MaxX))
                Assert.That(navigator.PlanToStation(s.Layout.SpawnPoints[0],station.Id),Is.Not.Null,station.Id.Value);
            var routes=new ReturnRouteField(s.Expedition);
            foreach(var p in new[]{new BasePosition(71,10),new BasePosition(90,40),new BasePosition(-400,200),new BasePosition(10000,-10000)})
            {
                Assert.That(s.Layout.IsAdmissible(p,.35),Is.True,p.ToString());
                Assert.That(s.Expedition.TryGetZone(p,out var zone),Is.True);
                Assert.That(zone,Is.EqualTo(CrewZone.Exterior));
                Assert.That(routes.TryGetPathMeters(p,out double meters),Is.True,"return route from extended terrain");
                Assert.That(meters,Is.GreaterThanOrEqualTo(p.DistanceTo(s.Expedition.Airlock.ExteriorEntry)));
            }
            Assert.That(s.Expedition.TryGetZone(s.Layout.SpawnPoints[0],out var inside),Is.True);
            Assert.That(inside,Is.EqualTo(CrewZone.Interior),"expanded exterior fallback cannot override the cabin");
        }
        [Test]
        public void RecycledTerrainKeepsGlobalTileCoordinatesAndAConstantRendererCount()
        {
            var texture=new Texture2D(2,2);
            var sprite=Sprite.Create(texture,new Rect(0,0,2,2),new Vector2(.5f,.5f),1);
            var a=new GameObject("client A terrain");var b=new GameObject("client B terrain");
            try
            {
                b.transform.position=new Vector3(500,-300,0);
                var first=a.AddComponent<LunarSurfaceView>();var second=b.AddComponent<LunarSurfaceView>();
                first.Configure(sprite,null);second.Configure(sprite,null);
                first.Recenter(23.99,-.01);second.Recenter(24.01,.01);
                var left=a.GetComponentsInChildren<SpriteRenderer>();var right=b.GetComponentsInChildren<SpriteRenderer>();
                Assert.That(left.Length,Is.EqualTo(49));Assert.That(right.Length,Is.EqualTo(49));
                var shared=left.Select(x=>x.transform.position).Intersect(right.Select(x=>x.transform.position)).ToArray();
                Assert.That(shared.Length,Is.GreaterThan(0),"clients at opposite sides of tile boundaries must agree on overlapping cells");
                foreach(var position in shared)
                {
                    Assert.That((position.x-12)%24,Is.EqualTo(0));
                    Assert.That((position.y-8)%16,Is.EqualTo(0));
                    var l=left.First(x=>x.transform.position==position);var r=right.First(x=>x.transform.position==position);
                    Assert.That(l.flipX,Is.EqualTo(r.flipX));Assert.That(l.flipY,Is.EqualTo(r.flipY));
                    Assert.That(l.flipX,Is.EqualTo((LunarSurfaceView.Column(position.x)&1)!=0));
                    Assert.That(l.flipY,Is.EqualTo((LunarSurfaceView.Row(position.y)&1)!=0));
                }
                first.Recenter(10000,-10000);first.Recenter(23.99,-.01);
                Assert.That(a.GetComponentsInChildren<SpriteRenderer>().Length,Is.EqualTo(49));
                foreach(var position in shared) Assert.That(left.Any(x=>x.transform.position==position),Is.True,"returning recovers the same world coordinates");
            }
            finally{Object.DestroyImmediate(a);Object.DestroyImmediate(b);Object.DestroyImmediate(sprite);Object.DestroyImmediate(texture);}
        }
    }
}
