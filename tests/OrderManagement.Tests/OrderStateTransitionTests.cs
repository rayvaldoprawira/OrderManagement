using FluentAssertions;
using OrderManagement.Domain.Entities;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.Exceptions;

namespace OrderManagement.Tests;

public class OrderStateTransitionTests
{
    [Fact]
    public void PendingOrder_CanTransitionToConfirmed()
    {
        var order = new Order { Status = OrderStatus.Pending };
        order.UpdateStatus(OrderStatus.Confirmed);
        order.Status.Should().Be(OrderStatus.Confirmed);
    }

    [Fact]
    public void PendingOrder_CanTransitionToCancelled()
    {
        var order = new Order { Status = OrderStatus.Pending };
        order.UpdateStatus(OrderStatus.Cancelled);
        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public void ConfirmedOrder_CanTransitionToShipped()
    {
        var order = new Order { Status = OrderStatus.Confirmed };
        order.UpdateStatus(OrderStatus.Shipped);
        order.Status.Should().Be(OrderStatus.Shipped);
    }

    [Fact]
    public void ConfirmedOrder_CanTransitionToCancelled()
    {
        var order = new Order { Status = OrderStatus.Confirmed };
        order.UpdateStatus(OrderStatus.Cancelled);
        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public void ShippedOrder_CanTransitionToDelivered()
    {
        var order = new Order { Status = OrderStatus.Shipped };
        order.UpdateStatus(OrderStatus.Delivered);
        order.Status.Should().Be(OrderStatus.Delivered);
    }

    [Theory]
    [InlineData(OrderStatus.Delivered, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Shipped)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Pending)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Confirmed)]
    public void TerminalStates_CannotBeModified(OrderStatus fromStatus, OrderStatus toStatus)
    {
        var order = new Order { Status = fromStatus };
        var act = () => order.UpdateStatus(toStatus);
        act.Should().Throw<InvalidOrderStateTransitionException>()
            .WithMessage("*terminal state*");
    }

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Delivered)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Delivered)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Cancelled)]
    public void InvalidIntermediateTransitions_ShouldThrowException(OrderStatus fromStatus, OrderStatus toStatus)
    {
        var order = new Order { Status = fromStatus };
        var act = () => order.UpdateStatus(toStatus);
        act.Should().Throw<InvalidOrderStateTransitionException>()
            .WithMessage("*Invalid status transition*");
    }

    [Theory]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Cancelled)]
    public void CancelOrder_WhenNotPendingOrConfirmed_ShouldThrowException(OrderStatus currentStatus)
    {
        var order = new Order { Status = currentStatus };
        var act = () => order.Cancel();
        act.Should().Throw<InvalidOrderStateTransitionException>()
            .WithMessage("*Only Pending or Confirmed orders can be cancelled*");
    }
}
