using IIoT.SharedKernel.Messaging;
using MediatR;

namespace IIoT.Services.Common.Contracts;

public interface IHumanRequest<out TResponse> : IRequest<TResponse>;

public interface IDeviceRequest<out TResponse> : IRequest<TResponse>;

public interface IAnonymousBootstrapRequest<out TResponse> : IRequest<TResponse>;

public interface IHumanCommand<out TResponse> : ICommand<TResponse>, IHumanRequest<TResponse>;

public interface IHumanQuery<out TResponse> : IQuery<TResponse>, IHumanRequest<TResponse>;

public interface IDeviceCommand<out TResponse> : ICommand<TResponse>, IDeviceRequest<TResponse>;

public interface IDeviceQuery<out TResponse> : IQuery<TResponse>, IDeviceRequest<TResponse>;

public interface IAnonymousBootstrapQuery<out TResponse> : IQuery<TResponse>, IAnonymousBootstrapRequest<TResponse>;
