using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Equinox76561198048419394.RailSystem.Util;
using FluentAssertions;
using NUnit.Framework;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;

namespace Equinox76561198048419394.RailSystem.Tests
{
    [TestFixture]
    public class RootEntityRefTest
    {
        private MyHierarchyComponent Create(long id)
        {
            var entity = new MyEntity {EntityId = id};
            return entity.Hierarchy;
        }

        [Test]
        public void Test()
        {
            var a = Create(0xa);
            var b = Create(0xb);
            var c = Create(0xc);
            var d = Create(0xd);

            var rootRef = new RootEntityRef(a);
            ValueTuple<MyHierarchyComponent, MyHierarchyComponent>? observedEvent = null;
            rootRef.RootParentChanged += (self, oldParent, newParent) =>
            {
                self.Should().BeEquivalentTo(a, "Self should be the component it was constructed for");
                observedEvent.Should().BeNull("Should only see a single event");
                observedEvent = ValueTuple.Create(oldParent, newParent);
            };

            rootRef.RootParent.Should().BeEquivalentTo(a, "Self should have a root parent of itself");

            b.AddChild(a.Entity);
            observedEvent.Should().Be(ValueTuple.Create(a, b), "Should have seen a reparent event from a to b");
            rootRef.RootParent.Should().BeEquivalentTo(b, "Parent should now be B");
            observedEvent = null;


            c.AddChild(b.Entity);
            observedEvent.Should().Be(ValueTuple.Create(b, c), "Should have seen a reparent event from b to c");
            rootRef.RootParent.Should().BeEquivalentTo(c, "Parent should now be C");
            observedEvent = null;


            d.AddChild(a.Entity);
            observedEvent.Should().Be(ValueTuple.Create(c, d), "Should have seen a reparent event from c to d");
            rootRef.RootParent.Should().BeEquivalentTo(d, "Parent should now be d");
            observedEvent = null;
            
            // Manually assert: ParentChanged event of b and c isn't set
        }
    }
}