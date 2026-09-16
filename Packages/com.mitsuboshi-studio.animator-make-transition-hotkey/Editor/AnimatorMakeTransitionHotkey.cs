#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

[InitializeOnLoad]
public static class AnimatorMakeTransitionHotkey
{
    private const string AnimatorWindowTypeName = "UnityEditor.Graphs.AnimatorControllerTool";
    private const string StateMachineGraphTypeName = "UnityEditor.Graphs.AnimationStateMachine.Graph";
    private const string AnimatorNodeNamespacePrefix = "UnityEditor.Graphs.AnimationStateMachine";

    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticFlags =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static FieldInfo s_GlobalEventHandlerField;
    private static Delegate s_OurGlobalHandler;

    private static FieldInfo s_StateMachineGraphField;
    private static FieldInfo s_StateNodeLookupField;
    private static FieldInfo s_StateMachineNodeLookupField;
    private static FieldInfo s_GraphNodesField;

    private static readonly Dictionary<Type, MethodInfo> s_MakeTransitionMethodCache =
        new Dictionary<Type, MethodInfo>();

    static AnimatorMakeTransitionHotkey()
    {
        EditorApplication.delayCall += Install;
        AssemblyReloadEvents.beforeAssemblyReload += Uninstall;
    }

    private static void Install()
    {
        CacheAnimatorInternals();
        InstallGlobalKeyHook();
    }

    private static void CacheAnimatorInternals()
    {
        Type animatorWindowType = FindType(AnimatorWindowTypeName);
        Type graphType = FindType(StateMachineGraphTypeName);

        if (animatorWindowType == null || graphType == null)
        {
            Debug.LogError("[Animator Make Transition Hotkey] Required Animator internal types were not found.");
            return;
        }

        s_StateMachineGraphField = animatorWindowType.GetField("stateMachineGraph", InstanceFlags);
        s_StateNodeLookupField = graphType.GetField("m_StateNodeLookup", InstanceFlags);
        s_StateMachineNodeLookupField = graphType.GetField("m_StateMachineNodeLookup", InstanceFlags);
        s_GraphNodesField = FindFieldInHierarchy(graphType, "nodes");
    }

    private static void InstallGlobalKeyHook()
    {
        UninstallGlobalKeyHook();

        s_GlobalEventHandlerField =
            typeof(EditorApplication).GetField("globalEventHandler", StaticFlags);

        if (s_GlobalEventHandlerField == null)
        {
            Debug.LogError("[Animator Make Transition Hotkey] EditorApplication.globalEventHandler was not found.");
            return;
        }

        MethodInfo callbackMethod =
            typeof(AnimatorMakeTransitionHotkey).GetMethod(nameof(OnGlobalEditorEvent), StaticFlags);

        if (callbackMethod == null)
            return;

        try
        {
            Type delegateType = s_GlobalEventHandlerField.FieldType;
            s_OurGlobalHandler = Delegate.CreateDelegate(delegateType, callbackMethod);

            Delegate existing = s_GlobalEventHandlerField.GetValue(null) as Delegate;
            Delegate combined = existing == null
                ? s_OurGlobalHandler
                : Delegate.Combine(s_OurGlobalHandler, existing);

            s_GlobalEventHandlerField.SetValue(null, combined);
        }
        catch (Exception ex)
        {
            s_OurGlobalHandler = null;
            Debug.LogException(ex);
        }
    }

    private static void Uninstall()
    {
        UninstallGlobalKeyHook();
    }

    private static void UninstallGlobalKeyHook()
    {
        if (s_GlobalEventHandlerField == null || s_OurGlobalHandler == null)
            return;

        try
        {
            Delegate existing = s_GlobalEventHandlerField.GetValue(null) as Delegate;
            if (existing != null)
            {
                Delegate withoutOurs = Delegate.Remove(existing, s_OurGlobalHandler);
                s_GlobalEventHandlerField.SetValue(null, withoutOurs);
            }
        }
        catch { }

        s_OurGlobalHandler = null;
    }

    private static void OnGlobalEditorEvent()
    {
        Event evt = Event.current;
        if (evt == null)
            return;

        if (evt.rawType != EventType.KeyDown || evt.keyCode != KeyCode.T)
            return;

        if ((evt.modifiers &
             (EventModifiers.Control | EventModifiers.Command |
              EventModifiers.Shift | EventModifiers.Alt)) != 0)
            return;

        if (EditorGUIUtility.editingTextField)
            return;

        EditorWindow animatorWindow = EditorWindow.focusedWindow;
        if (!IsAnimatorWindow(animatorWindow))
            return;

        if (!TryStartMakeTransition(animatorWindow))
            return;

        evt.Use();
        animatorWindow.Repaint();
    }

    private static bool TryStartMakeTransition(EditorWindow animatorWindow)
    {
        if (!EnsureInternalsReady())
            return false;

        object graph;
        try
        {
            graph = s_StateMachineGraphField.GetValue(animatorWindow);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            return false;
        }

        if (graph == null)
            return false;

        object sourceNode = ResolveSelectedTransitionSourceNode(graph);
        if (sourceNode == null)
            return false;

        MethodInfo method = GetMakeTransitionMethod(sourceNode.GetType());
        if (method == null)
            return false;

        try
        {
            method.Invoke(sourceNode, null);
            return true;
        }
        catch (TargetInvocationException ex)
        {
            Debug.LogException(ex.InnerException ?? ex);
            return false;
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            return false;
        }
    }

    private static object ResolveSelectedTransitionSourceNode(object graph)
    {
        UnityEngine.Object activeObject = Selection.activeObject;

        // State
        AnimatorState state = activeObject as AnimatorState;
        if (state != null)
        {
            object node = LookupNode(graph, s_StateNodeLookupField, state);
            if (NodeCanMakeTransition(node))
                return node;
        }

        // State Machine
        AnimatorStateMachine stateMachine = activeObject as AnimatorStateMachine;
        if (stateMachine != null)
        {
            object node = LookupNode(graph, s_StateMachineNodeLookupField, stateMachine);
            if (NodeCanMakeTransition(node))
                return node;
        }

        // Entry / Any State / その他の内部Nodeが直接Selectionに乗る場合
        if (IsAnimatorGraphNode(activeObject) && NodeCanMakeTransition(activeObject))
            return activeObject;

        // Graph内の選択済みNodeを総当たり。
        // MakeTransitionCallback() を持つものだけ対象にする。
        foreach (object node in EnumerateGraphNodes(graph))
        {
            if (!IsAnimatorGraphNode(node))
                continue;

            if (!IsUnityObjectSelected(node))
                continue;

            if (NodeCanMakeTransition(node))
                return node;
        }

        return null;
    }

    private static object LookupNode(
        object graph,
        FieldInfo lookupField,
        UnityEngine.Object key)
    {
        if (graph == null || lookupField == null || key == null)
            return null;

        try
        {
            IDictionary lookup = lookupField.GetValue(graph) as IDictionary;
            if (lookup == null || !lookup.Contains(key))
                return null;

            return lookup[key];
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<object> EnumerateGraphNodes(object graph)
    {
        if (graph == null || s_GraphNodesField == null)
            yield break;

        object value;
        try
        {
            value = s_GraphNodesField.GetValue(graph);
        }
        catch
        {
            yield break;
        }

        IEnumerable enumerable = value as IEnumerable;
        if (enumerable == null)
            yield break;

        foreach (object node in enumerable)
        {
            if (node != null)
                yield return node;
        }
    }

    private static bool IsAnimatorGraphNode(object candidate)
    {
        if (candidate == null)
            return false;

        Type type = candidate.GetType();
        string fullName = type.FullName ?? "";

        return fullName.StartsWith(
                   AnimatorNodeNamespacePrefix,
                   StringComparison.Ordinal)
               && type.Name.EndsWith("Node", StringComparison.Ordinal);
    }

    private static bool IsUnityObjectSelected(object candidate)
    {
        UnityEngine.Object obj = candidate as UnityEngine.Object;
        if (obj == null)
            return false;

        try
        {
            return Selection.Contains(obj.GetInstanceID());
        }
        catch
        {
            return false;
        }
    }

    private static bool NodeCanMakeTransition(object node)
    {
        return node != null && GetMakeTransitionMethod(node.GetType()) != null;
    }

    private static MethodInfo GetMakeTransitionMethod(Type nodeType)
    {
        if (nodeType == null)
            return null;

        MethodInfo cached;
        if (s_MakeTransitionMethodCache.TryGetValue(nodeType, out cached))
            return cached;

        MethodInfo result = null;

        for (Type current = nodeType; current != null; current = current.BaseType)
        {
            MethodInfo exact = current.GetMethod(
                "MakeTransitionCallback",
                InstanceFlags | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);

            if (exact != null && exact.ReturnType == typeof(void))
            {
                result = exact;
                break;
            }
        }

        if (result == null)
        {
            for (Type current = nodeType; current != null; current = current.BaseType)
            {
                MethodInfo[] methods;
                try
                {
                    methods = current.GetMethods(
                        InstanceFlags | BindingFlags.DeclaredOnly);
                }
                catch
                {
                    continue;
                }

                foreach (MethodInfo method in methods)
                {
                    if (method.ReturnType != typeof(void) ||
                        method.GetParameters().Length != 0)
                        continue;

                    string name = method.Name ?? "";
                    if (name.IndexOf("Make", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        name.IndexOf("Transition", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        result = method;
                        break;
                    }
                }

                if (result != null)
                    break;
            }
        }

        s_MakeTransitionMethodCache[nodeType] = result;
        return result;
    }

    private static bool EnsureInternalsReady()
    {
        if (s_StateMachineGraphField != null &&
            s_StateNodeLookupField != null &&
            s_StateMachineNodeLookupField != null &&
            s_GraphNodesField != null)
            return true;

        CacheAnimatorInternals();

        return s_StateMachineGraphField != null &&
               s_StateNodeLookupField != null &&
               s_StateMachineNodeLookupField != null &&
               s_GraphNodesField != null;
    }

    private static bool IsAnimatorWindow(EditorWindow window)
    {
        return window != null &&
               window.GetType().FullName == AnimatorWindowTypeName;
    }

    private static FieldInfo FindFieldInHierarchy(Type type, string fieldName)
    {
        for (Type current = type; current != null; current = current.BaseType)
        {
            FieldInfo field = current.GetField(
                fieldName,
                InstanceFlags | BindingFlags.DeclaredOnly);

            if (field != null)
                return field;
        }

        return null;
    }

    private static Type FindType(string fullName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null)
                    return type;
            }
            catch { }
        }

        return null;
    }
}
#endif
