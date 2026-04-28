using IIoT.SharedKernel.Messaging;
using MediatR;

namespace IIoT.Services.Contracts;

/// <summary>
/// 人员端请求标记。
/// 只表达“请求来自 human/* 入口”这一层语义，供管道行为识别请求来源使用。
/// </summary>
public interface IHumanRequest<out TResponse> : IRequest<TResponse>;

/// <summary>
/// 设备端请求标记。
/// 只表达“请求来自 edge/* 入口”这一层语义，供管道行为识别请求来源使用。
/// </summary>
public interface IDeviceRequest<out TResponse> : IRequest<TResponse>;

/// <summary>
/// 匿名 bootstrap 请求标记。
/// 这类请求允许在未登录状态下执行，但只能用于设备启动引导链路。
/// </summary>
public interface IAnonymousBootstrapRequest<out TResponse> : IRequest<TResponse>;

/// <summary>
/// 人员端命令。
/// 等价于“这是一个 Command，并且它属于 human/* 请求”。
/// </summary>
public interface IHumanCommand<out TResponse> : ICommand<TResponse>, IHumanRequest<TResponse>;

/// <summary>
/// 人员端查询。
/// 等价于“这是一个 Query，并且它属于 human/* 请求”。
/// </summary>
public interface IHumanQuery<out TResponse> : IQuery<TResponse>, IHumanRequest<TResponse>;

/// <summary>
/// 设备端命令。
/// 等价于“这是一个 Command，并且它属于 edge/* 请求”。
/// </summary>
public interface IDeviceCommand<out TResponse> : ICommand<TResponse>, IDeviceRequest<TResponse>;

/// <summary>
/// 设备端查询。
/// 等价于“这是一个 Query，并且它属于 edge/* 请求”。
/// </summary>
public interface IDeviceQuery<out TResponse> : IQuery<TResponse>, IDeviceRequest<TResponse>;

/// <summary>
/// 匿名 bootstrap 查询。
/// 等价于“这是一个 Query，并且它属于匿名设备引导请求”。
/// 当前 bootstrap 只开放查询语义，不开放匿名写操作。
/// </summary>
public interface IAnonymousBootstrapQuery<out TResponse> : IQuery<TResponse>, IAnonymousBootstrapRequest<TResponse>;

/// <summary>
/// 匿名 bootstrap 命令。
/// 用于设备匿名引导链路上的受控写操作，例如 refresh token 轮换。
/// </summary>
public interface IAnonymousBootstrapCommand<out TResponse> : ICommand<TResponse>, IAnonymousBootstrapRequest<TResponse>;
