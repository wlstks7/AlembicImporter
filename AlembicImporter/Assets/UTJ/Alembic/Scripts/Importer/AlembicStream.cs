using System;
using System.Collections.Generic;
using UnityEngine;

namespace UTJ.Alembic
{
    public class AlembicStream : IDisposable
    {
        static List<AlembicStream> s_Streams = new List<AlembicStream>();

        public static void DisconnectStreamsWithPath(string path)
        {
            var fullPath = Application.streamingAssetsPath + path;
            AbcAPI.aiClearContextsWithPath(fullPath);
            s_Streams.ForEach(s => {
                if (s.m_StreamDesc.pathToAbc == path)
                {
                    s.m_StreamInterupted = true;
                    s.m_Context = default(AbcAPI.aiContext);
                    s.m_Loaded = false;   
                }
            });
        } 

        public static void RemapStreamsWithPath(string oldPath , string newPath)
        {
            s_Streams.ForEach(s =>
            {
                if (s.m_StreamDesc.pathToAbc == oldPath)
                {
                    s.m_StreamInterupted = true;
                    s.m_StreamDesc.pathToAbc = newPath;
                }
            } );
        } 

        public static void ReconnectStreamsWithPath(string path)
        {
            s_Streams.ForEach(s =>
            {
                if (s.m_StreamDesc.pathToAbc == path)
                {
                    s.m_StreamInterupted = false;
                }
                    
            } );
        }

        public AlembicTreeNode alembicTreeRoot;
        private AlembicStreamDescriptor m_StreamDesc;
        private AbcAPI.aiConfig m_Config;
        private AbcAPI.aiContext m_Context;
        private float m_Time;
        private bool m_Loaded;
        private bool m_StreamInterupted;

        public AlembicStream(GameObject rootGo, AlembicStreamDescriptor streamDesc)
        {
            m_Config.SetDefaults();
            alembicTreeRoot = new AlembicTreeNode() { streamDescriptor = streamDesc, linkedGameObj = rootGo };
            m_StreamDesc = streamDesc;
        }

        public bool AbcIsValid()
        {
            return (m_Context.ptr != (IntPtr)0);
        }

        public void AbcUpdateConfigElements(AlembicTreeNode node = null)
        {
            if (node == null)
                node = alembicTreeRoot;
            using (var o = node.alembicObjects.GetEnumerator())
            {
                while (o.MoveNext())
                {
                    o.Current.Value.AbcUpdateConfig();
                }
            }
            using (var c = node.children.GetEnumerator())
            {
                while (c.MoveNext())
                {
                    AbcUpdateConfigElements(c.Current);
                }
            }
        }

        public void AbcUpdateElements( AlembicTreeNode node = null )
        {
            if (node == null)
                node = alembicTreeRoot;
            using (var o = node.alembicObjects.GetEnumerator())
            {
                while (o.MoveNext())
                {
                    o.Current.Value.AbcUpdate();
                }
            }
            using (var c = node.children.GetEnumerator())
            {
                while (c.MoveNext())
                {
                    AbcUpdateElements(c.Current);
                }
            }
        }

        public float abcStartTime
        {
            get {
                return AbcIsValid() ? AbcAPI.aiGetStartTime(m_Context) : 0;
            }
        }
        public int abcFrameCount
        {
            get {
                return AbcIsValid() ? AbcAPI.aiGetFrameCount(m_Context) : 0;
            }
        }

        public float abcEndTime
        {
            get
            {
                return AbcIsValid() ? AbcAPI.aiGetEndTime(m_Context) : 0;
            }
        }

        // returns false if the context needs to be recovered.
        public bool AbcUpdate(float time, float motionScale,bool interpolateSamples)
        {
            if (m_StreamInterupted) return true;
            
            if (!AbcIsValid() || !m_Loaded) return false;

            m_Time = time;
            m_Config.interpolateSamples = interpolateSamples;
            m_Config.vertexMotionScale = motionScale;
            AbcAPI.aiSetConfig(m_Context, ref m_Config);
            AbcUpdateConfigElements();

            AbcAPI.aiUpdateSamples(m_Context, m_Time);
            AbcUpdateElements();

            return true;
        }
   
        public void AbcLoad()
        {
            m_Time = 0.0f;

            m_Context = AbcAPI.aiCreateContext(alembicTreeRoot.linkedGameObj.GetInstanceID());

            var settings = m_StreamDesc.settings;
            m_Config.swapHandedness = settings.swapHandedness;
            m_Config.swapFaceWinding = settings.swapFaceWinding;
            m_Config.normalsMode = settings.normals;
            m_Config.tangentsMode = settings.tangents;
            m_Config.cacheSamples = settings.cacheSamples;
            m_Config.turnQuadEdges = settings.turnQuadEdges;
            m_Config.aspectRatio = AbcAPI.GetAspectRatio(settings.cameraAspectRatio);
#if UNITY_2017_3_OR_NEWER
            if (settings.use32BitsIndexBuffer)
                m_Config.splitUnit = 0x7fffff;
            else
#endif
                m_Config.splitUnit = 65000;
            AbcAPI.aiSetConfig(m_Context, ref m_Config);

            m_Loaded = AbcAPI.aiLoad(m_Context,Application.streamingAssetsPath + m_StreamDesc.pathToAbc);

            if (m_Loaded)
            {
                AbcAPI.UpdateAbcTree(m_Context, alembicTreeRoot, m_Time);
                AlembicStream.s_Streams.Add(this);
            }
            else
            {
                Debug.LogError("failed to load alembic at " + Application.streamingAssetsPath + m_StreamDesc.pathToAbc);
            }
        }

        public void Dispose()
        {
            AlembicStream.s_Streams.Remove(this);
            if (alembicTreeRoot != null)
            {
                alembicTreeRoot.Dispose();
                alembicTreeRoot = null;
            }

            if (AbcIsValid())
            {
                AbcAPI.aiDestroyContext(m_Context);
                m_Context = default(AbcAPI.aiContext);
            }
        }
    }
}
