using System.Reflection;
using Microsoft.AspNetCore.Components;

namespace Battleship.Web.Tests.Infrastructure;

internal static class TestComponent
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static object? Get(object target, string name)
    {
        var type = target.GetType();
        while (type is not null)
        {
            var property = type.GetProperty(name, Flags);
            if (property is not null && property.GetIndexParameters().Length == 0)
            {
                return property.GetValue(target);
            }

            var field = type.GetField(name, Flags);
            if (field is not null)
            {
                return field.GetValue(target);
            }

            type = type.BaseType;
        }

        throw new MissingMemberException(target.GetType().FullName, name);
    }

    public static void Set(object target, string name, object? value)
    {
        var member = FindMember(target.GetType(), name);
        if (member is PropertyInfo property)
        {
            property.SetValue(target, value);
            return;
        }

        ((FieldInfo)member).SetValue(target, value);
    }

    public static void SetEnum(object target, string name, string value)
    {
        var member = FindMember(target.GetType(), name);
        var type = member is PropertyInfo property ? property.PropertyType : ((FieldInfo)member).FieldType;
        var parsed = Enum.Parse(type, value);
        if (member is PropertyInfo targetProperty)
        {
            targetProperty.SetValue(target, parsed);
        }
        else
        {
            ((FieldInfo)member).SetValue(target, parsed);
        }
    }

    public static object? Call(object target, string name, params object?[] arguments)
    {
        var method = FindMethod(target.GetType(), name, arguments.Length);
        return method.Invoke(target, arguments);
    }

    public static void Inject(object target, IServiceProvider services)
    {
        var type = target.GetType();
        while (type is not null)
        {
            foreach (var property in type.GetProperties(Flags))
            {
                if (property.GetCustomAttribute<InjectAttribute>() is null)
                {
                    continue;
                }

                var service = services.GetService(property.PropertyType)
                    ?? throw new InvalidOperationException($"No service registered for {property.PropertyType}.");
                property.SetValue(target, service);
            }

            type = type.BaseType;
        }
    }

    public static async Task CallAsync(object target, string name, params object?[] arguments)
    {
        var result = Call(target, name, arguments);
        if (result is Task task)
        {
            await task;
        }
    }

    public static async Task<T> CallAsync<T>(object target, string name, params object?[] arguments)
    {
        var result = Call(target, name, arguments);
        return await (Task<T>)result!;
    }

    private static MemberInfo FindMember(Type? type, string name)
    {
        while (type is not null)
        {
            var property = type.GetProperty(name, Flags);
            if (property is not null && property.GetIndexParameters().Length == 0)
            {
                return property;
            }

            var field = type.GetField(name, Flags);
            if (field is not null)
            {
                return field;
            }

            type = type.BaseType;
        }

        throw new MissingMemberException(type?.FullName, name);
    }

    private static MethodInfo FindMethod(Type? type, string name, int parameterCount)
    {
        while (type is not null)
        {
            var methods = type
                .GetMethods(Flags | BindingFlags.Static)
                .Where(method => method.Name == name && method.GetParameters().Length == parameterCount)
                .ToArray();
            if (methods.Length == 1)
            {
                return methods[0];
            }

            if (methods.Length > 1)
            {
                throw new AmbiguousMatchException($"Multiple methods named {name} take {parameterCount} arguments.");
            }

            type = type.BaseType;
        }

        throw new MissingMethodException(type?.FullName, name);
    }
}
