using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;
using System.Linq;
using UnityEngine;
using System;
#if UNITY_EDITOR
using UnityEditor;
#endif


namespace SpaceVideo
{
    public class VideoPlayer : MonoBehaviour
    {
        public Material baseMaterial;
        public Material otherControllersMaterial;
        public Transform baseActor;

        struct Actor { internal int vgo_index; internal Transform tr; }

        Video video;
        List<Actor> actors;
        float start_time, next_time;
        Material[] materials;
        Mesh[] meshes;
        SteamVR_RenderModel[] head_ctrls;
        SteamVR_TrackedObject.EIndex ctrl_index = SteamVR_TrackedObject.EIndex.None;


        public void VPlay(string filename)
        {
            Video video = new Video();
            var fs = new BinaryReader(new FileStream(filename, FileMode.Open));
            try
            {
                var deserializer = new Deserializer(fs);
                deserializer.Deserialize(video);
            }
            finally
            {
                fs.Close();
            }
            VPlay(video);
        }

        public void VPlay(Video new_video)
        {
            UnloadVideo();

            video = new_video;
            start_time = Time.unscaledTime;
            next_time = start_time;

            LoadVideo();
        }

        void LoadVideo()
        {
            actors = new List<Actor>();
            materials = new Material[video.materials.Length];
            meshes = new Mesh[video.meshes.Length];

            head_ctrls = new SteamVR_RenderModel[3];
            for (int i = 0; i < head_ctrls.Length; i++)
            {
                var go = new GameObject();
                go.transform.SetParent(transform);
                var rm = go.AddComponent<SteamVR_RenderModel>();
                rm.createComponents = false;
                rm.updateDynamically = false;
                rm.enabled = false;
                go.SetActive(false);
                head_ctrls[i] = rm;
            }
            gameObject.SetActive(true);
        }

        void UnloadVideo()
        {
            if (actors != null)
                foreach (var actor in actors)
                    DestroyImmediate(actor.tr.gameObject);
            actors = null;

            if (materials != null)
                foreach (var mat in materials)
                    if (mat != null)
                        DestroyImmediate(mat);
            materials = null;

            if (meshes != null)
                foreach (var mesh in meshes)
                    if (mesh != null)
                        DestroyImmediate(mesh);
            meshes = null;

            if (head_ctrls != null)
                foreach (var hc in head_ctrls)
                    DestroyImmediate(hc.gameObject);
            head_ctrls = null;

            video = null;
            gameObject.SetActive(false);
        }

        void ExpandToTransform(Transform target, float[] source)
        {
            target.localPosition = new Vector3(source[0], source[1], source[2]);
            target.localRotation = new Quaternion(source[3], source[4], source[5], source[6]);
            target.localScale = new Vector3(source[7], source[8], source[9]);
        }

        Vector3[] ExpandToVectorArray(float[] array)
        {
            Vector3[] result = new Vector3[array.Length / 3];
            for (int i = 0; i < result.Length; i++)
                result[i] = new Vector3(array[i * 3], array[i * 3 + 1], array[i * 3 + 2]);
            return result;
        }

        Material LoadMaterial(int index)
        {
            var mat = materials[index];
            if (mat == null)
            {
                var src = video.materials[index];

                mat = new Material(baseMaterial);
                mat.color = new Color(src.color[0], src.color[1], src.color[2], src.color[3]);
                materials[index] = mat;
            }
            return mat;
        }

        Mesh LoadMesh(int index)
        {
            var mesh = meshes[index];
            if (mesh == null)
            {
                var src = video.meshes[index];

                mesh = new Mesh();
                mesh.vertices = ExpandToVectorArray(src.vertices);
                mesh.normals = ExpandToVectorArray(src.normals);
                mesh.subMeshCount = src.submeshes.Length;
                for (int i = 0; i < src.submeshes.Length; i++)
                {
                    mesh.SetIndices(src.submeshes[i].indices,
                        (MeshTopology)src.submeshes[i].topology, i,
                        calculateBounds: false);
                }
                mesh.RecalculateBounds();
            }
            return mesh;
        }

        private void Update()
        {
            if (Time.unscaledTime < next_time)
                return;

            if (video == null)
            {
                UnloadVideo();
                return;
            }

            int frame_index = (int)((Time.unscaledTime - start_time) / video.time_step);
            if (frame_index >= video.frames.Length)
            {
                UnloadVideo();
                return;
            }

            VideoFrame frame = video.frames[frame_index];
            next_time = start_time + (frame_index + 1) * video.time_step;

            if (frame.destroy_meshes != null)
                foreach (var mesh_id in frame.destroy_meshes)
                {
                    DestroyImmediate(meshes[mesh_id]);
                    meshes[mesh_id] = null;
                }

            while (actors.Count < frame.go.Length)
                actors.Add(new Actor { vgo_index = -1, tr = Instantiate(baseActor, this.transform) });

            while (actors.Count > frame.go.Length)
                DestroyImmediate(actors.Pop().tr.gameObject);

            int j = 0;
            foreach (var vgo_index in frame.go)
            {
                var actor = actors[j++];
                if (actor.vgo_index == vgo_index)
                    continue;     /* no updates here */

                Transform tr = actor.tr;
                VideoGameObject vgo = video.gameobjects[vgo_index];

                ExpandToTransform(tr, vgo.tr);
                tr.GetComponent<MeshFilter>().sharedMesh = LoadMesh(vgo.mesh);

                Material[] mat = new Material[vgo.mats.Length];
                for (int i = 0; i < mat.Length; i++)
                    mat[i] = LoadMaterial(vgo.mats[i]);
                tr.GetComponent<MeshRenderer>().sharedMaterials = mat;

                actor.vgo_index = vgo_index;
            }

            foreach (var ctrl in BaroqueUI.Baroque.GetControllers())
            {
                var trobj = ctrl.GetComponent<SteamVR_TrackedObject>();
                if (trobj.index != SteamVR_TrackedObject.EIndex.None)
                    ctrl_index = trobj.index;
            }

            for (int k = 0; k < 3; k++)
            {
                if ((k + 1) * 10 <= frame.head_ctrls.Length)
                {
                    ExpandToTransform(head_ctrls[k].transform, frame.head_ctrls.Skip(k * 10).Take(10).ToArray());
                    if (!head_ctrls[k].gameObject.activeSelf)
                    {
                        head_ctrls[k].index = k == 0 ? SteamVR_TrackedObject.EIndex.Hmd : ctrl_index;
                        head_ctrls[k].gameObject.SetActive(true);
                        head_ctrls[k].enabled = true;
                        head_ctrls[k].UpdateModel();
                    }
                    var rend = head_ctrls[k].GetComponent<MeshRenderer>();
                    if (rend != null)
                        rend.sharedMaterial = otherControllersMaterial;
                }
                else
                    head_ctrls[k].gameObject.SetActive(false);
            }
        }


        class Deserializer : ISerializeInfo
        {
            BinaryReader reader;

            internal Deserializer(BinaryReader reader)
            {
                this.reader = reader;
            }

            internal void Deserialize(ISerialize obj)
            {
                obj.EnumAttributes(this);
            }

            public void SerializeInt(ref int value)
            {
                value = reader.ReadInt32();
            }

            public void SerializeFloat(ref float value)
            {
                value = reader.ReadSingle();
            }

            public void SerializeIntArray(ref int[] value)
            {
                int length = reader.ReadUInt16();
                if (length == 0xFFFF)
                {
                    value = null;
                    return;
                }
                if (length < 0xFFFE)
                {
                    value = new int[length];
                    for (int i = 0; i < length; i++)
                        value[i] = reader.ReadInt16();
                }
                else
                {
                    length = reader.ReadInt32();
                    value = new int[length];
                    for (int i = 0; i < length; i++)
                        value[i] = reader.ReadInt32();
                }
            }

            public void SerializeFloatArray(ref float[] value)
            {
                int length = reader.ReadInt32();
                if (length == -1)
                    value = null;
                else
                {
                    value = new float[length];
                    for (int i = 0; i < length; i++)
                        value[i] = reader.ReadSingle();
                }
            }

            public void SerializeObjsArray<T>(ref T[] value) where T : class, ISerialize, new()
            {
                int length = reader.ReadInt32();
                if (length == -1)
                    value = null;
                else
                {
                    value = new T[length];
                    for (int i = 0; i < length; i++)
                    {
                        T x = value[i] = new T();
                        Deserialize(x);
                    }
                }
            }
        }
    }

    
#if UNITY_EDITOR
    [CustomEditor(typeof(VideoPlayer))]
    public class VideoPlayerEditor : Editor
    {
        int selected_file_number;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            GUILayout.Label("");

            var strings_list = new List<string>();
            foreach (var fullname in System.IO.Directory.GetFiles(VideoRecorder.GetLocationsPath(), "*.video"))
            {
                var fn = System.IO.Path.GetFileName(fullname);
                strings_list.Add(fn);
            }
            selected_file_number = EditorGUILayout.Popup(selected_file_number, strings_list.ToArray());
            GUILayout.Label("");

            if (!Application.isPlaying)
            {
                GUILayout.Label("Video playing is only available in Play mode.");
                return;
            }

            if (GUILayout.Button("Play"))
                StartPlaying(VideoRecorder.GetLocationsPath() + "/" + strings_list[selected_file_number]);
        }

        void StartPlaying(string filename)
        {
            var vp = target as VideoPlayer;
            vp.VPlay(filename);
        }
    }
#endif
}
