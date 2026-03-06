using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace IIoT.SharedKernel.Messaging;

public interface ICommand<out TResponse> : IRequest<TResponse>;

public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>;