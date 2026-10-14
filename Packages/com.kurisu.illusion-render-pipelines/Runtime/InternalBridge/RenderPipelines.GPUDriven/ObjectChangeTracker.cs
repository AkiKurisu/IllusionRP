using System;
using System.Collections.Generic;
using Unity.Collections;

namespace UnityEngine.Experimental.Rendering
{
    internal sealed class ObjectChangeTracker : IDisposable
    {
        private readonly ObjectDispatcher _dispatcher = new() { maxDispatchHistoryFramesCount = int.MaxValue };

        public void TrackSceneObjects<T>() where T : Object
        {
            _dispatcher.EnableTypeTracking<T>(ObjectDispatcher.TypeTrackingFlags.SceneObjects);
        }

        public void TrackAllObjects<T>() where T : Object
        {
            _dispatcher.EnableTypeTracking<T>(ObjectDispatcher.TypeTrackingFlags.All);
        }

        public void TrackHierarchy<T>() where T : Component
        {
            _dispatcher.EnableTransformTracking<T>(ObjectDispatcher.TransformTrackingType.Hierarchy);
        }

        public void ConsumeChanges<T>(List<Object> changed, List<EntityId> destroyed) where T : Object
        {
            changed.Clear();
            _dispatcher.GetTypeChangesAndClear<T>(changed, out var changedIds, out var destroyedIds, Allocator.Temp);
            foreach (var id in destroyedIds)
                destroyed.Add(id);
            changedIds.Dispose();
            destroyedIds.Dispose();
        }

        public void ConsumeHierarchyChanges<T>(List<Object> changed) where T : Component
        {
            changed.Clear();
            var data = _dispatcher.GetTransformChangesAndClear<T>(ObjectDispatcher.TransformTrackingType.Hierarchy, Allocator.Temp);
            Resources.EntityIdsToObjectList(data.transformedID, changed);
            data.Dispose();
        }

        public void Dispose()
        {
            _dispatcher.Dispose();
        }
    }
}
