﻿/**************************************************************************************
**
**  Copyright (C) 2006 Thomas Luft, University of Konstanz. All rights reserved.
**
**  This file was part of the Ivy Generator Tool.
**
**  This program is free software; you can redistribute it and/or modify it
**  under the terms of the GNU General Public License as published by the
**  Free Software Foundation; either version 2 of the License, or (at your
**  option) any later version.
**  This program is distributed in the hope that it will be useful, but
**  WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
**  or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License
**  for more details.
**  You should have received a copy of the GNU General Public License along
**  with this program; if not, write to the Free Software Foundation,
**  Inc., 51 Franklin St, Fifth Floor, Boston, MA 02110, USA 
**
***************************************************************************************/

// subsequent modifications:
// (C) 2016 Weng Xiao Yi https://github.com/phoenixzz/IvyGenerator
// (C) 2019 Robert Yang https://github.com/radiatoryang/hedera

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using System.IO;
using UnityEngine.SceneManagement;

namespace Hedera
{
	[InitializeOnLoad]
    public class IvyCore
    {
		public static IvyCore Instance;

		public static double lastRefreshTime { get; private set; }
		static double refreshInterval = 0.1;

		public static List<IvyBehavior> ivyBehaviors = new List<IvyBehavior>();

		const int TERRAIN_SEARCH_COUNT = 64;
		static Dictionary<TerrainCollider, Terrain> colliderToTerrain = new Dictionary<TerrainCollider, Terrain>();
		static Vector3[] terrainSearchDisc = new Vector3[TERRAIN_SEARCH_COUNT];

        // called on InitializeOnLoad
        static IvyCore()
        {
            if (Instance == null)
            {
                Instance = new IvyCore();
				colliderToTerrain = new Dictionary<TerrainCollider, Terrain>();
				terrainSearchDisc = new Vector3[TERRAIN_SEARCH_COUNT];
            }
            EditorApplication.update += Instance.OnEditorUpdate;
			ivyBehaviors.Clear();
        }

		// TODO:
		// x add "show advanced"
		// - add "leaf sun tilt"
		// - save mesh in external asset?
		// x move profile editor to profile inspector
		// - try to find default materials for new ivy profiles (or add "fix" button)
		// - display warning about editing in 2018.3 prefab space

		// - let user replace mesh with OBJ (one-time operation)
		// x vertex colors
		// - let user specify branch sides (2-6)
		// - test: merging should use mesh cache

		// - test build out

        void OnEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup > lastRefreshTime + refreshInterval)
            {
                lastRefreshTime = EditorApplication.timeSinceStartup;
				CacheTerrainColliderStuff();
				adhesionMeshCache.Clear();
                foreach (var ivyB in ivyBehaviors) {
					if ( ivyB == null || ivyB.profileAsset == null) {
						continue;
					}
					foreach ( var ivy in ivyB.ivyGraphs) {
						if ( ivy.isGrowing ) {
							GrowIvyStep(ivy, ivyB.profileAsset.ivyProfile);
							if ( ivy.generateMeshDuringGrowth ) {
								IvyMesh.GenerateMesh(ivy, ivyB.profileAsset.ivyProfile);
							}
						}
						if ( !ivy.isGrowing && ivy.generateMeshDuringGrowth && ivyB.profileAsset.ivyProfile.useLightmapping && ivy.dirtyUV2s ) {
							IvyMesh.GenerateMesh( ivy, ivyB.profileAsset.ivyProfile, true);
						}
					}
				}
            }
			
        }

		static void CacheTerrainColliderStuff () {
			colliderToTerrain.Clear();
			foreach ( var terrain in Terrain.activeTerrains ) {
				colliderToTerrain.Add( terrain.GetComponent<TerrainCollider>(), terrain);
			}

			for ( int i=0; i<TERRAIN_SEARCH_COUNT; i++) {
				terrainSearchDisc[i] = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.value;
			}
		}

        [MenuItem("Hedera/Create New Ivy Generator...")]
        public static void NewAssetFromHederaMenu()
        {
            CreateNewAsset("");
        }

		public static IvyProfileAsset CreateNewAsset(string path = "Assets/NewIvyProfile.asset") {
			if ( path == "") {
				path = EditorUtility.SaveFilePanelInProject("Hedera: Create New Ivy Profile .asset file...", "NewIvyProfile.asset", "asset", "Choose where in your project to save the new ivy profile file.");
			}

			IvyProfileAsset asset = ScriptableObject.CreateInstance<IvyProfileAsset>();

            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();

            EditorUtility.FocusProjectWindow();

            Selection.activeObject = asset;
			return asset;
		}

		public static IvyDataAsset GetDataAsset(GameObject forObject) {
			var scene= forObject.scene;
			var mainFolder = Path.GetDirectoryName( scene.path );
			string path = mainFolder + "/" + scene.name + "/HederaData.asset";

			var asset = AssetDatabase.LoadAssetAtPath<IvyDataAsset>(path);
			if ( asset == null) {
				asset = CreateNewDataAsset( mainFolder, scene.name, Path.GetDirectoryName(path));
			}
			return asset;
		}

		public static IvyDataAsset CreateNewDataAsset(string mainFolder, string sceneName, string path) {
			if ( !AssetDatabase.IsValidFolder(path) ) {
				var folderGUID = AssetDatabase.CreateFolder( mainFolder, sceneName );
				path = AssetDatabase.GUIDToAssetPath(folderGUID);
			}

			IvyDataAsset asset = ScriptableObject.CreateInstance<IvyDataAsset>();
            AssetDatabase.CreateAsset(asset, path + "/HederaData.asset");
            AssetDatabase.SaveAssets();
			return asset;
		}

		[MenuItem("Hedera/Force-Stop All Ivy Growing")]
        public static void ForceStopGrowing()
        {
            foreach ( var gen in ivyBehaviors ) {
				foreach (var ivy in gen.ivyGraphs ) {
					ivy.isGrowing = false;
				}
			}
        }

        public static IvyGraph SeedNewIvyGraph(IvyProfile ivyProfile, Vector3 seedPos, Vector3 primaryGrowDir, Vector3 adhesionVector, Transform root, bool generateMeshPreview=false)
        {
            var graph = new IvyGraph();
	        graph.ResetMeshData();
	        graph.roots.Clear();
			graph.seedPos = seedPos;
			graph.generateMeshDuringGrowth = generateMeshPreview;
			graph.rootBehavior = root;

	        IvyNode tmpNode = new IvyNode();
	        tmpNode.p = Vector3.zero; //seedPos;
	        tmpNode.g = primaryGrowDir;
	        tmpNode.c = adhesionVector;
	        tmpNode.s = 0.0f;
			tmpNode.cS = 0f;
	        tmpNode.fS = 0.0f;
	        tmpNode.cl = true;

	        IvyRoot tmpRoot = new IvyRoot();
	        tmpRoot.nodes.Add( tmpNode );
	        tmpRoot.isAlive = true;
			graph.isGrowing = true;
	        tmpRoot.parents = 0;
	        graph.roots.Add( tmpRoot );

			if ( graph.generateMeshDuringGrowth ) {
				IvyMesh.GenerateMesh( graph, ivyProfile );
				Undo.RegisterCreatedObjectUndo( graph.rootGO, "Hedera > Paint Ivy");
			}

            return graph;
        }

		public static void ForceIvyGrowth(IvyGraph graph, IvyProfile ivyProfile, Vector3 newPos, Vector3 newNormal) {
			newPos -= graph.seedPos; // convert to local space

			// find the nearest root end node, and continue off of it
			// var closestRoot = graph.roots.OrderBy( root => Vector3.Distance( newPos, root.nodes.Last().localPos ) ).FirstOrDefault();
			// if ( closestRoot == null ) {
			// 	return;
			// }
			var closestRoot = graph.roots[0];

			var lastNode = closestRoot.nodes[ closestRoot.nodes.Count-1 ];
			var growVector = newPos - lastNode.p;

			var newNode = new IvyNode();

			newNode.p = newPos;
			newNode.g = (0.5f * lastNode.g + 0.5f * growVector.normalized).normalized;
			//newNode.adhesionVector = ComputeAdhesion( newPos, ivyProfile );
			//if ( newNode.adhesionVector.sqrMagnitude < 0.01f ) {
				newNode.c = -newNormal;
			//}
			newNode.s = lastNode.s + growVector.magnitude;
			newNode.cS = lastNode.cS + growVector.magnitude;
			newNode.fS = 0f;
			newNode.cl = true;

			closestRoot.nodes.Add( newNode );
			closestRoot.useCachedBranchData = false;
			closestRoot.useCachedLeafData = false;
			// TryGrowIvyBranch( graph, ivyProfile, closestRoot, newNode );

			closestRoot.debugLineSegmentsList.Add(lastNode.p + graph.seedPos);
			closestRoot.debugLineSegmentsList.Add(newPos + graph.seedPos);
			closestRoot.debugLineSegmentsArray = closestRoot.debugLineSegmentsList.ToArray();

			if ( graph.generateMeshDuringGrowth ) {
				IvyMesh.GenerateMesh( graph, ivyProfile );
			}
		}

		public static void ForceRandomIvyBranch ( IvyGraph graph, IvyProfile ivyProfile ) {
			var randomRoot = graph.roots[0];
			var randomNode = randomRoot.nodes[Random.Range(0, randomRoot.nodes.Count)];
			var randomLength = randomNode.cS + Mathf.Lerp(ivyProfile.minLength * 1.5f, ivyProfile.maxLength, Random.value);
			TryGrowIvyBranch( graph, ivyProfile, randomRoot, randomNode, randomLength);
		}

	    public static void GrowIvyStep(IvyGraph graph, IvyProfile ivyProfile)
        {
			// if there are no longer any live roots, then we're dead
			if ( graph.isGrowing ) {
				graph.isGrowing = graph.roots.Where( root => root.isAlive ).Count() > 0;
			}
			if ( !graph.isGrowing ) {
				return;
			}

	        //lets grow
	        foreach (var root in graph.roots)
	        {
		        //process only roots that are alive
		        if (!root.isAlive) 
                    continue;

                IvyNode lastNode = root.nodes[root.nodes.Count-1];

		        //let the ivy die, if the maximum float length is reached
				if ( lastNode.cS > ivyProfile.maxLength || (lastNode.cS > Mathf.Max(root.forceMinLength, ivyProfile.minLength) && lastNode.fS > ivyProfile.maxFloatLength) ) {
                    // Debug.LogFormat("root death! cum dist: {0:F2}, floatLength {1:F2}", lastNode.lengthCumulative, lastNode.floatingLength);
					root.isAlive = false;
					continue;
				}

                //grow vectors: primary direction, random influence, and adhesion of scene objectss

                //primary vector = weighted sum of previous grow vectors plus a little bit upwards
                Vector3 primaryVector = Vector3.Normalize(lastNode.g * 2f + Vector3.up);

                //random influence plus a little upright vector
				Vector3 exploreVector = lastNode.p - root.nodes[0].p;
				if ( exploreVector.magnitude > 1f ) {
					exploreVector = exploreVector.normalized;
				}
				exploreVector *= Mathf.PingPong( root.nodes[0].p.sqrMagnitude * root.parents + lastNode.cS * 0.69f, 1f);
                Vector3 randomVector = (Random.onUnitSphere * 0.5f + exploreVector).normalized;

                //adhesion influence to the nearest triangle = weighted sum of previous adhesion vectors
                Vector3 adhesionVector = ComputeAdhesion(lastNode.p + graph.seedPos, ivyProfile);
				if ( adhesionVector.sqrMagnitude <= 0.01f) {
					adhesionVector = lastNode.c;
				}

                //compute grow vector
                Vector3 growVector = ivyProfile.ivyStepDistance * 
				Vector3.Normalize(
					primaryVector * ivyProfile.primaryWeight 
					+ randomVector * Mathf.Max(0.01f, ivyProfile.randomWeight) 
					+ adhesionVector * ivyProfile.adhesionWeight
				);

                //gravity influence
                Vector3 gravityVector = ivyProfile.ivyStepDistance * Vector3.down * ivyProfile.gravityWeight;
                //gravity depends on the floating length
                gravityVector *= Mathf.Pow(lastNode.fS / ivyProfile.maxFloatLength, 0.7f);

                //next possible ivy node

                //climbing state of that ivy node, will be set during collision detection
                bool climbing = false;

                //compute position of next ivy node
                Vector3 newPos = lastNode.p + growVector + gravityVector;

                //combine alive state with result of the collision detection, e.g. let the ivy die in case of a collision detection problem
                Vector3 adhesionFromRaycast = adhesionVector;

				// convert newPos to world position, just for the collision calc
				newPos += graph.seedPos;
				root.isAlive = root.isAlive && ComputeCollision( 0.01f, lastNode.p + graph.seedPos, ref newPos, ref climbing, ref adhesionFromRaycast, ivyProfile.collisionMask);
				newPos -= graph.seedPos;

                //update grow vector due to a changed newPos
                growVector = newPos - lastNode.p - gravityVector;

				// +graph.seedPos to convert back to world space
				root.debugLineSegmentsList.Add(lastNode.p + graph.seedPos);
				root.debugLineSegmentsList.Add(newPos + graph.seedPos);
				// cache line segments
				root.debugLineSegmentsArray = root.debugLineSegmentsList.ToArray();

                //create next ivy node
                IvyNode newNode = new IvyNode();

                newNode.p = newPos;
                newNode.g = (0.5f * lastNode.g + 0.5f * growVector.normalized).normalized;
                newNode.c = adhesionVector; //Vector3.Lerp(adhesionVector, adhesionFromRaycast, 0.5f);
                newNode.s = lastNode.s + (newPos - lastNode.p).magnitude;
				newNode.cS = lastNode.cS + (newPos - lastNode.p).magnitude;
                newNode.fS = climbing ? 0.0f : lastNode.fS + (newPos - lastNode.p).magnitude;
                newNode.cl = climbing;

		        root.nodes.Add( newNode );
				root.useCachedBranchData = false;
				root.useCachedLeafData = false;

				var randomNode = root.nodes[Random.Range(0, root.nodes.Count)];
				if ( TryGrowIvyBranch( graph, ivyProfile, root, randomNode ) ) {
					break;
				}
	        }

        }

		static bool TryGrowIvyBranch (IvyGraph graph, IvyProfile ivyProfile, IvyRoot root, IvyNode fromNode, float forceMinLength = -1f) {
			//weight depending on ratio of node length to total length
			float weight = 1f; //Mathf.PerlinNoise( fromNode.localPos.x + fromNode.lengthCumulative, fromNode.length + fromNode.localPos.y + fromNode.localPos.z); // - ( Mathf.Cos( fromNode.length / root.nodes[root.nodes.Count-1].length * 2.0f * Mathf.PI) * 0.5f + 0.5f );
			var nearbyRootCount = graph.roots.Where( r => (r.nodes[0].p - fromNode.p).sqrMagnitude < ivyProfile.ivyStepDistance * ivyProfile.ivyStepDistance ).Count();
			if ( forceMinLength <= 0f ) {
				if ( graph.roots.Count >= ivyProfile.maxBranchesTotal 
					|| nearbyRootCount > ivyProfile.branchingProbability * 2.5f
					|| root.childCount > ivyProfile.branchingProbability * 3.5f
					|| root.nodes.Count < 3
					|| root.parents > ivyProfile.branchingProbability * 9f
					|| ivyProfile.maxLength - fromNode.cS < ivyProfile.minLength 
					|| Random.value * Mathf.Clamp(weight, 0f, 1f - ivyProfile.branchingProbability) > ivyProfile.branchingProbability
				) {
					return false;
				}
			}

			//new ivy node
			IvyNode newRootNode = new IvyNode();
			newRootNode.p = fromNode.p;
			newRootNode.g = Vector3.Lerp( fromNode.g, Vector3.up, 0.5f).normalized;
			newRootNode.c = fromNode.c;
			newRootNode.s = 0.0f;
			newRootNode.cS = forceMinLength > 0f ? 0f : fromNode.cS;
			newRootNode.fS = forceMinLength > 0f ? 0f : fromNode.fS;
			newRootNode.cl = true;

			//new ivy root
			IvyRoot newRoot = new IvyRoot();
			newRoot.nodes.Add( newRootNode );
			newRoot.isAlive = true;
			newRoot.parents = root.parents + 1;
			newRoot.forceMinLength = forceMinLength;
			
			graph.roots.Add( newRoot );
			root.childCount++;
			return true;
		}

	    /** compute the adhesion of scene objects at a point pos*/
		static Dictionary<Mesh, Vector3[]> adhesionMeshCache = new Dictionary<Mesh, Vector3[]>();
	    static Vector3 ComputeAdhesion(Vector3 pos, IvyProfile ivyProfile)
        {
	        Vector3 adhesionVector = Vector3.zero;

	        float minDistance = ivyProfile.maxAdhesionDistance;

			// find nearest colliders
			var nearbyColliders = Physics.OverlapSphere( pos, ivyProfile.maxAdhesionDistance, ivyProfile.collisionMask, QueryTriggerInteraction.Ignore);

			// find closest point on each collider
			foreach ( var col in nearbyColliders ) {
				Vector3 closestPoint = pos + Vector3.down * ivyProfile.maxAdhesionDistance * 1.1f;
				// ClosestPoint does not work on non-convex mesh colliders so let's just pick the closest vertex
				if ( col is MeshCollider && !((MeshCollider)col).convex ) {
					// if we haven't already cached mesh vertices, or it's a bad cache for some reason, then re-cache it
					var mesh = ((MeshCollider)col).sharedMesh;
					if ( !adhesionMeshCache.ContainsKey(mesh) ) {
						adhesionMeshCache.Add( mesh, mesh.vertices );
					} 

					// check for a close-enough vertex
					float sqrMeshDistance = ivyProfile.maxAdhesionDistance * ivyProfile.maxAdhesionDistance * 4f;
					for( int i=0; i<adhesionMeshCache[mesh].Length; i++) {
						if ( Vector3.SqrMagnitude( pos - col.transform.TransformPoint(adhesionMeshCache[mesh][i])) < sqrMeshDistance ) {
							closestPoint = col.transform.TransformPoint(adhesionMeshCache[mesh][i]);
							sqrMeshDistance = Vector3.SqrMagnitude( pos - closestPoint);
						}
					}
					// closestPoint = col.transform.TransformPoint( ((MeshCollider)col).sharedMesh.vertices.OrderBy( vert => Vector3.SqrMagnitude(pos - col.transform.TransformPoint(vert)) ).FirstOrDefault() );

					// try to get surface normal towards nearest vertex
					var meshColliderHit = new RaycastHit();
					if ( col.Raycast( new Ray(pos, closestPoint - pos), out meshColliderHit, ivyProfile.maxAdhesionDistance) ) {
						closestPoint = pos - meshColliderHit.normal * meshColliderHit.distance;
					}
				} // ClosestPoint doesn't work on TerrainColliders either...
				else if ( col is TerrainCollider ) {
					// based on cache of TerrainColliders, search surrounding points until we find a close enough position
					var terrain = colliderToTerrain[(TerrainCollider)col];
					closestPoint = pos;
					closestPoint.y = terrain.SampleHeight( closestPoint );
					Vector3 closestSearchPoint = closestPoint;
					Vector3 currentSearchPoint = Vector3.zero;

					for ( int i=0; i<terrainSearchDisc.Length; i++) {
						currentSearchPoint = closestPoint + terrainSearchDisc[i] * ivyProfile.maxAdhesionDistance;
						currentSearchPoint.y = terrain.SampleHeight( currentSearchPoint );
						if ( Vector3.SqrMagnitude(pos - currentSearchPoint) < Vector3.SqrMagnitude(pos - closestSearchPoint) ) {
							closestSearchPoint = currentSearchPoint;
							// close enough, early out
							if ( Vector3.SqrMagnitude(pos - currentSearchPoint) < ivyProfile.ivyStepDistance * ivyProfile.ivyStepDistance ) {
								break;
							}
						}
					}
					
					currentSearchPoint = closestSearchPoint + Vector3.down * 0.25f;
					var terrainRayHit = new RaycastHit();
					if ( Physics.Raycast( pos, currentSearchPoint - pos, out terrainRayHit, minDistance, ivyProfile.collisionMask, QueryTriggerInteraction.Ignore) ) {
						closestPoint = pos - terrainRayHit.normal * Vector3.Distance(closestSearchPoint, pos);
					}
				} else {
					closestPoint = col.ClosestPoint( pos );
				}

				// see if the distance is closer than the closest distance so far
				float distance = Vector3.Distance(pos, closestPoint);
				if ( distance < minDistance ) {
					minDistance = distance;
					adhesionVector = (closestPoint - pos).normalized;
				    adhesionVector *= 1.0f - distance / ivyProfile.maxAdhesionDistance; //distance dependent adhesion vector
					// close enough, early out
					if ( Vector3.SqrMagnitude(pos - closestPoint) < ivyProfile.ivyStepDistance * ivyProfile.ivyStepDistance ) {
						break;
					}
				}
			}
	        return adhesionVector;
        }

		public static void RegenerateDebugLines( IvyRoot root) {
			root.debugLineSegmentsArray = new Vector3[(root.nodes.Count-1)*2];
			if ( root.nodes.Count <= 2 ) {
				return;
			}

			int nodeCounter = 0;
			for( int i=0; i<root.debugLineSegmentsArray.Length; i+=2) {
				root.debugLineSegmentsArray[i] = root.nodes[nodeCounter].p;
				root.debugLineSegmentsArray[i+1] = root.nodes[nodeCounter+1].p;
				nodeCounter++;
			}
		}

	    /** computes the collision detection for an ivy segment oldPos->newPos, newPos will be modified if necessary */
        static bool ComputeCollision(float stepDistance, Vector3 oldPos, ref Vector3 newPos, ref bool isClimbing, ref Vector3 adhesionVector, LayerMask collisionMask)
        {
	        //reset climbing state
	        isClimbing = false;
	        bool intersection;
	        int deadlockCounter = 0;

	        do {
		        intersection = false;

				// new raycast collision test
				RaycastHit newRayHit = new RaycastHit();
				if ( Physics.Raycast( oldPos, newPos - oldPos, out newRayHit, Vector3.Distance(oldPos,newPos), collisionMask, QueryTriggerInteraction.Ignore) )
				{                    
					newPos += newRayHit.normal * stepDistance;
					adhesionVector = -newRayHit.normal;
					intersection = true;
					isClimbing = true;
				}

		        // abort climbing and growing if the root is stuck in a crack or something
		        if (deadlockCounter++ > 16)
		        {
			        return false;
		        }
  	        }
	        while (intersection);

	        return true;
        }

		public static IvyGraph MergeVisibleIvyGraphs (IvyBehavior ivyBehavior, IvyProfile ivyProfile, bool rebuildMesh = true) {
			var graphsToMerge = ivyBehavior.ivyGraphs.Where( graph => graph.isVisible ).ToList();
			if ( graphsToMerge == null || graphsToMerge.Count == 0) {
				return null;
			}

			var mainGraph = graphsToMerge[0];

			for ( int i=0; i< ivyBehavior.ivyGraphs.Count; i++ ) {
				var graph = ivyBehavior.ivyGraphs[i];
				if ( !graph.isVisible || graph == mainGraph ) {
					continue;
				}

				// convert merged graph's localPos to mainGraph's localPos
				foreach ( var root in graph.roots ) {
					foreach ( var node in root.nodes ) {
						node.p += graph.seedPos - mainGraph.seedPos;
					}
				}
				mainGraph.roots.AddRange( graph.roots );

				if ( graph.rootGO != null) {
					DestroyObject( graph.rootGO );
				}

				ivyBehavior.ivyGraphs.Remove(graph);
				i--;
			}

			if ( rebuildMesh ) {
				Undo.RegisterFullObjectHierarchyUndo( mainGraph.rootGO, "Hedera > Merge Visible");
				IvyMesh.GenerateMesh( mainGraph, ivyProfile, ivyProfile.useLightmapping, true );
			}

			return mainGraph;
		}

		// this is all needed for Unity 2018.3 or later, due to the new prefab workflow... if earlier, then does nothing
		// static string rootPrefabPath = "";
		// static GameObject rootPrefabObject;
		// public static bool isEditingNewPrefab { get { return rootPrefabObject != null;} }
		// code from https://forum.unity.com/threads/destroying-a-gameobject-inside-a-prefab-instance-is-not-allowed.555868/
		// public static IvyBehavior StartDestructiveEdit (IvyBehavior ivyBehavior, bool applyAllOverrides = false) {
		// 	#if UNITY_2018_3_OR_NEWER
		// 	if ( PrefabUtility.IsPartOfPrefabInstance( ivyBehavior ) ) {
		// 		rootPrefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot( ivyBehavior );
		// 		if ( applyAllOverrides ) {
		// 			var checkChildrenForOverrides = new List<GameObject>();
		// 			foreach ( var graph in ivyBehavior.ivyGraphs ) {
		// 				checkChildrenForOverrides.Add( graph.rootGO );
		// 				if ( graph.leafMF != null ) {
		// 					checkChildrenForOverrides.Add( graph.leafMF.gameObject );
		// 				}
		// 				checkChildrenForOverrides.Add( graph.branchMF.gameObject );
		// 			}
		// 			foreach ( var child in checkChildrenForOverrides ) {
		// 				if ( PrefabUtility.IsAddedGameObjectOverride( child ) ) {
		// 					PrefabUtility.ApplyAddedGameObject( child, rootPrefabPath, InteractionMode.AutomatedAction);
		// 				}
		// 			}
		// 			PrefabUtility.ApplyObjectOverride( ivyBehavior, rootPrefabPath, InteractionMode.AutomatedAction);
		// 		}
		// 		rootPrefabObject = PrefabUtility.LoadPrefabContents( rootPrefabPath );
		// 		var rootBehavior = PrefabUtility.GetCorrespondingObjectFromSource<IvyBehavior>(ivyBehavior);
		// 		// find corresponding IvyBehavior, because there might be multiple ivy behaviors in the prefab
		// 		return rootBehavior;
		// 	}
		// 	#endif
		// 	rootPrefabPath = "";
		// 	rootPrefabObject = null;
		// 	return ivyBehavior;
		// }

		public static void DestroyObject (GameObject go, string undoMessage = "Hedera > Destroy Ivy") {
			Undo.SetCurrentGroupName( undoMessage );
			// from https://forum.unity.com/threads/programmatically-destroy-gameobjects-in-prefabs.591907/#post-3953059
			#if UNITY_2018_3_OR_NEWER
			if ( PrefabUtility.IsPartOfPrefabInstance(go.transform) ) {
				// if a part of a prefab instance then get the instance handle
				Object prefabInstance = PrefabUtility.GetPrefabInstanceHandle(go.transform);
				// destroy the handle
				Object.DestroyImmediate(prefabInstance);
				Object.DestroyImmediate(go);
				return;
			}
			#endif
			Undo.DestroyObjectImmediate( go );
		}

		// public static void CommitDestructiveEdit () {
		// 	#if UNITY_2018_3_OR_NEWER
		// 	if ( !string.IsNullOrEmpty(rootPrefabPath) && rootPrefabObject != null ) {
		// 		PrefabUtility.SaveAsPrefabAsset(rootPrefabObject, rootPrefabPath);
		// 		PrefabUtility.UnloadPrefabContents(rootPrefabObject);
		// 	}
		// 	#endif
		// 	// does nothing if earlier than Unity 2018.3
		// }



    }
}
