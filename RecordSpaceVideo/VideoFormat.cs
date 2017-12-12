using System;


namespace SpaceVideo
{
    public interface ISerializeInfo
    {
        void SerializeInt(ref int value);
        void SerializeFloat(ref float value);
        void SerializeIntArray(ref int[] value);
        void SerializeFloatArray(ref float[] value);
        void SerializeObjsArray<T>(ref T[] value) where T : class, ISerialize, new();
    }

    public interface ISerialize
    {
        void EnumAttributes(ISerializeInfo info);
    }

    public class Video : ISerialize
    {
        public const int VERSION = 134289700;

        public float time_step;
        public VideoFrame[] frames;
        public VideoMaterial[] materials;
        public VideoMesh[] meshes;
        public VideoGameObject[] gameobjects;

        public void EnumAttributes(ISerializeInfo info)
        {
            int version = VERSION;
            info.SerializeInt(ref version);
            if (version != VERSION)
                throw new NotImplementedException("bad version number " + version);

            info.SerializeFloat(ref time_step);
            info.SerializeObjsArray(ref frames);
            info.SerializeObjsArray(ref materials);
            info.SerializeObjsArray(ref meshes);
            info.SerializeObjsArray(ref gameobjects);
        }
    }

    public class VideoFrame : ISerialize
    {
        public int[] destroy_meshes;
        public float[] head_ctrls;    /* 1 to 3 times a transform (which is 10 floats each) */
        public int[] go;

        public void EnumAttributes(ISerializeInfo info)
        {
            info.SerializeIntArray(ref destroy_meshes);
            info.SerializeFloatArray(ref head_ctrls);
            info.SerializeIntArray(ref go);
        }
    }

    public class VideoGameObject : ISerialize
    {
        public float[] tr;
        public int mesh;
        public int[] mats;

        public void EnumAttributes(ISerializeInfo info)
        {
            info.SerializeFloatArray(ref tr);
            info.SerializeInt(ref mesh);
            info.SerializeIntArray(ref mats);
        }
    }

    public class VideoMaterial : ISerialize
    {
        public float[] color;

        public void EnumAttributes(ISerializeInfo info)
        {
            info.SerializeFloatArray(ref color);
        }
    }

    public class VideoMesh : ISerialize
    {
        public float[] vertices;
        public float[] normals;
        public VideoSubmesh[] submeshes;

        public void EnumAttributes(ISerializeInfo info)
        {
            info.SerializeFloatArray(ref vertices);
            info.SerializeFloatArray(ref normals);
            info.SerializeObjsArray(ref submeshes);
        }
    }

    public class VideoSubmesh : ISerialize
    {
        public int topology;
        public int[] indices;

        public void EnumAttributes(ISerializeInfo info)
        {
            info.SerializeInt(ref topology);
            info.SerializeIntArray(ref indices);
        }
    }
}