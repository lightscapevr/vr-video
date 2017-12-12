using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
#if UNITY_EDITOR
using UnityEditor;
#endif
using BaroqueUI;


namespace SpaceVideo
{
    public class VideoRecorder : MonoBehaviour
    {
        public enum State
        {
            RECORDING,
            NOT_RECORDING
        }

        public State state = State.NOT_RECORDING;

        public float recordFrequency = 20f;
        public string[] excludeTags;

        List<VideoFrame> rec_frames;
        Dictionary<GameObject, int> rec_vgo_mapping;
        List<VideoGameObject> rec_gameobjects;
        List<int> rec_go;
        List<VideoMesh> rec_meshes;
        List<VideoMaterial> rec_materials;
        List<int> rec_destroy_meshes;

        Dictionary<Mesh, int> meshes_id;
        Dictionary<Material, int> materials_id;
        float time_step;

        float[] FlattenTransform(Transform tr)
        {
            float[] result = new float[10];
            Vector3 p = tr.position;
            result[0] = p.x;
            result[1] = p.y;
            result[2] = p.z;
            Quaternion r = tr.rotation;
            result[3] = r.x;
            result[4] = r.y;
            result[5] = r.z;
            result[6] = r.w;
            Vector3 s = tr.lossyScale;
            result[7] = s.x;
            result[8] = s.y;
            result[9] = s.z;
            return result;
        }

        float[] FlattenVectorArray(Vector3[] array)
        {
            float[] result = new float[array.Length * 3];
            for (int i = 0; i < array.Length; i++)
            {
                Vector3 pt = array[i];
                result[i * 3 + 0] = pt.x;
                result[i * 3 + 1] = pt.y;
                result[i * 3 + 2] = pt.z;
            }
            return result;
        }

        int GetOrRecordMesh(Mesh mesh)
        {
            int result;
            if (!meshes_id.TryGetValue(mesh, out result))
            {
                result = rec_meshes.Count;

                var submeshes = new VideoSubmesh[mesh.subMeshCount];
                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    submeshes[i] = new VideoSubmesh
                    {
                        topology = (int)mesh.GetTopology(i),
                        indices = mesh.GetIndices(i),
                    };
                }
                rec_meshes.Add(new VideoMesh
                {
                    vertices = FlattenVectorArray(mesh.vertices),
                    normals = FlattenVectorArray(mesh.normals),
                    submeshes = submeshes,
                });
                meshes_id[mesh] = result;
            }
            return result;
        }

        int GetOrRecordMaterial(Material mat)
        {
            int result;
            if (!materials_id.TryGetValue(mat, out result))
            {
                result = rec_materials.Count;

                Color col = mat.color;
                rec_materials.Add(new VideoMaterial
                {
                    color = new float[] { col.r, col.g, col.b, col.a },
                });
                materials_id[mat] = result;
            }
            return result;
        }

        void RecordObjectState(GameObject go)
        {
            var filt = go.GetComponent<MeshFilter>();
            if (filt == null)
                return;

            var rend = go.GetComponent<MeshRenderer>();
            if (rend == null || !rend.enabled)
                return;

            var tr = FlattenTransform(go.transform);
            var mesh = GetOrRecordMesh(filt.sharedMesh);
            var rend_mats = rend.sharedMaterials;
            var mats = new int[rend_mats.Length];
            for (int i = 0; i < mats.Length; i++)
                mats[i] = GetOrRecordMaterial(rend_mats[i]);

            int vgo_index;
            if (rec_vgo_mapping.TryGetValue(go, out vgo_index) &&
                rec_gameobjects[vgo_index].tr.SequenceEqual(tr) &&
                rec_gameobjects[vgo_index].mesh == mesh &&
                rec_gameobjects[vgo_index].mats.SequenceEqual(mats))
            {
                /* can reuse the same vgo as before */
            }
            else
            {
                vgo_index = rec_gameobjects.Count;
                rec_gameobjects.Add(new VideoGameObject { tr = tr, mesh = mesh, mats = mats });
                rec_vgo_mapping[go] = vgo_index;
            }
            rec_go.Add(vgo_index);
        }

        float[] RecordRenderModels()
        {
            List<float> tr = new List<float>(FlattenTransform(Baroque.GetHeadTransform()));

            foreach (var ctrl in Baroque.GetControllers())
            {
                if (ctrl.isActiveAndEnabled)
                    tr.AddRange(FlattenTransform(ctrl.transform));
            }
            return tr.ToArray();
        }

        bool AcceptObject(GameObject go)
        {
            if (!go.activeSelf)
                return false;
            foreach (var excl in excludeTags)
                if (go.CompareTag(excl))
                    return false;
            if (go.GetComponent<SteamVR_RenderModel>() != null)
                return false;
            return true;
        }

        void RecordFrame()
        {
            rec_go.Clear();
            rec_destroy_meshes.Clear();

            var pending = new List<Transform>();
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
                pending.Add(go.transform);

            while (pending.Count > 0)
            {
                var tr = pending.Pop();
                if (AcceptObject(tr.gameObject))
                {
                    RecordObjectState(tr.gameObject);
                    int count = tr.childCount;
                    for (int i = 0; i < count; i++)
                        pending.Add(tr.GetChild(i));
                }
            }
            

            rec_frames.Add(new VideoFrame
            {
                go = rec_go.ToArray(),
                head_ctrls = RecordRenderModels(),
                destroy_meshes = (rec_destroy_meshes.Count > 0 ? rec_destroy_meshes.ToArray() : null),
            });
        }

        public void StartRecording()
        {
            rec_frames = new List<VideoFrame>();
            rec_meshes = new List<VideoMesh>();
            rec_materials = new List<VideoMaterial>();
            rec_gameobjects = new List<VideoGameObject>();
            meshes_id = new Dictionary<Mesh, int>();
            materials_id = new Dictionary<Material, int>();
            time_step = 1f / recordFrequency;

            rec_go = new List<int>();
            rec_destroy_meshes = new List<int>();
            rec_vgo_mapping = new Dictionary<GameObject, int>();

            StartCoroutine(FramesRecorder());
        }

        public Video StopRecording()
        {
            var result = new Video
            {
                time_step = time_step,
                frames = rec_frames.ToArray(),
                materials = rec_materials.ToArray(),
                meshes = rec_meshes.ToArray(),
                gameobjects = rec_gameobjects.ToArray(),
            };
            rec_frames = null;
            rec_meshes = null;
            rec_materials = null;
            rec_gameobjects = null;
            meshes_id = null;
            materials_id = null;
            rec_go = null;
            rec_destroy_meshes = null;
            rec_vgo_mapping = null;
            return result;
        }

        IEnumerator FramesRecorder()
        {
            yield return new WaitForEndOfFrame();

            float start_time = Time.unscaledTime;
            float next_time = start_time;
            while (rec_frames != null)
            {
                if (Time.unscaledTime >= next_time)
                {
                    RecordFrame();
                    next_time = start_time + rec_frames.Count * time_step;
                }
                yield return new WaitForEndOfFrame();
            }
        }

        public void RemoveMeshCache(Mesh mesh)
        {
            /* Remove 'mesh' from the recorded video's cache.  There are two reasons for
             * doing this:
             *
             *   1. if the mesh was just updated; otherwise, playback will not see the change.
             *
             *   2. if the mesh was destroyed, then it frees resources when doing playback.
             *      If you create and destroy meshes every frame, for example, then it is
             *      quite recommended to call this function, otherwise the playback will
             *      build up a cache of active meshes whose size increases very quickly
             *      (and Unity will likely not be happy about that).
             */
            int mesh_id;
            if (rec_destroy_meshes != null && meshes_id.TryGetValue(mesh, out mesh_id))
            {
                rec_destroy_meshes.Add(mesh_id);
                meshes_id.Remove(mesh);
            }
        }

        private void OnGUI()
        {
            if (state == State.NOT_RECORDING)
            {
                if (GUI.Button(new Rect(10, 10, 150, 30), "Start recording"))
                {
                    state = State.RECORDING;
                }
            }
            else
            {
                if (GUI.Button(new Rect(10, 10, 150, 30), "Stop recording"))
                {
                    state = State.NOT_RECORDING;
                }
            }
        }
    }


#if UNITY_EDITOR
    [CustomEditor(typeof(VideoRecorder))]
    public class VideoRecorderEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            GUILayout.Label("");

            if (!Application.isPlaying)
            {
                GUILayout.Label("Video recording is only available in Play mode.");
                return;
            }
            if (GUILayout.Button("Start recording"))
                StartRecording();
            if (GUILayout.Button("Stop recording"))
                StopRecording();
        }

        void StartRecording()
        {
            var vr = target as VideoRecorder;
            vr.StartRecording();
        }

        public static string GetLocationsPath()
        {
            return Application.dataPath + "/RecordSpaceVideo";
        }

        void StopRecording()
        {
            var vr = target as VideoRecorder;
            var msg = vr.StopRecording();

            int n = 1;
            string filename;
            while (true)
            {
                filename = GetLocationsPath() + "/Video" + n.ToString() + ".video";
                if (!System.IO.File.Exists(filename))
                    break;
                n += 1;
            }
            var fs = new BinaryWriter(new FileStream(filename, FileMode.Create));
            try
            {
                var serializer = new Serializer(fs);
                serializer.Serialize(msg);
            }
            finally
            {
                fs.Close();
            }
            EditorUtility.DisplayDialog("Video capture", "File created: " + filename, "Ok");
            AssetDatabase.Refresh();
        }


        class Serializer : ISerializeInfo
        {
            BinaryWriter writer;

            internal Serializer(BinaryWriter writer)
            {
                this.writer = writer;
            }

            internal void Serialize(ISerialize obj)
            {
                obj.EnumAttributes(this);
            }

            public void SerializeInt(ref int value)
            {
                writer.Write(value);
            }

            public void SerializeFloat(ref float value)
            {
                writer.Write(value);
            }

            public void SerializeIntArray(ref int[] value)
            {
                if (value == null)
                {
                    writer.Write((ushort)0xFFFF);
                    return;
                }
                if (value.Length < 0xFFFE && value.All(x => x >= -0x8000 && x <= 0x7FFF))
                {
                    writer.Write((ushort)value.Length);
                    foreach (var x in value)
                        writer.Write((short)x);
                }
                else
                {
                    writer.Write((ushort)0xFFFE);
                    writer.Write(value.Length);
                    foreach (var x in value)
                        writer.Write(x);
                }
            }

            public void SerializeFloatArray(ref float[] value)
            {
                if (value == null)
                {
                    writer.Write((int)-1);
                    return;
                }
                writer.Write(value.Length);
                foreach (var x in value)
                    writer.Write(x);
            }

            public void SerializeObjsArray<T>(ref T[] value) where T : class, ISerialize, new()
            {
                SerializeArray(value);
            }

            void SerializeArray(ISerialize[] value)
            {
                if (value == null)
                {
                    writer.Write((int)-1);
                    return;
                }
                writer.Write(value.Length);
                foreach (var x in value)
                    Serialize(x);
                return;
            }
        }
}
#endif


    public static class MyListExtension
    {
        public static T Pop<T>(this List<T> list)
        {
            T result = list[list.Count - 1];
            list.RemoveAt(list.Count - 1);
            return result;
        }

        public static T Last<T>(this List<T> list)
        {
            return list[list.Count - 1];
        }
    }
}