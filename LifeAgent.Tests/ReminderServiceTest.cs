using Xunit;
using LifeAgent.Api.Models;
using LifeAgent.Api.Services;
using LifeAgent.Api.Models.Exceptions;

namespace LifeAgent.Tests;

public class ReminderServiceTest
{
    [Fact]
    public void ValidateUpdate_PendingToCompleted_Succeeds()
    {
        // Arrange
        var reminder = new Reminder { Status = "pending", DueAt = DateTime.UtcNow.AddHours(2) };

        // Act
        bool statusChanged = ReminderService.ValidateUpdate(reminder, "completed", null);

        // Assert
        Assert.True(statusChanged);
    }

    [Fact]
    public void ValidateUpdate_PendingToCancelled_Succeeds()
    {
        // Arrange
        var reminder = new Reminder { Status = "pending", DueAt = DateTime.UtcNow.AddHours(2) };

        // Act
        bool statusChanged = ReminderService.ValidateUpdate(reminder, "cancelled", null);

        // Assert
        Assert.True(statusChanged);
    }

    [Fact]
    public void ValidateUpdate_PendingModifyDueAtOnly_Succeeds()
    {
        // Arrange
        var reminder = new Reminder { Status = "pending", DueAt = DateTime.UtcNow.AddHours(2) };

        // Act
        bool statusChanged = ReminderService.ValidateUpdate(reminder, null, DateTime.UtcNow.AddHours(3));

        // Assert
        Assert.False(statusChanged);
    }

    [Fact]
    public void ValidateUpdate_RevertToPending_ThrowsInvalidInputException()
    {
        // Arrange
        var reminder = new Reminder { Status = "completed" };

        // Act & Assert
        var ex = Assert.Throws<InvalidInputException>(() => 
            ReminderService.ValidateUpdate(reminder, "pending", null)
        );
        Assert.Contains("不允许将状态改回 pending", ex.Message);
    }

    [Fact]
    public void ValidateUpdate_InvalidStatusValue_ThrowsInvalidInputException()
    {
        // Arrange
        var reminder = new Reminder { Status = "pending" };

        // Act & Assert
        var ex = Assert.Throws<InvalidInputException>(() => 
            ReminderService.ValidateUpdate(reminder, "invalid_status", null)
        );
        Assert.Contains("非法的状态值", ex.Message);
    }

    [Fact]
    public void ValidateUpdate_CompletedToCancelled_ThrowsInvalidInputException()
    {
        // Arrange
        var reminder = new Reminder { Status = "completed" };

        // Act & Assert
        var ex = Assert.Throws<InvalidInputException>(() => 
            ReminderService.ValidateUpdate(reminder, "cancelled", null)
        );
        Assert.Contains("禁止更改", ex.Message);
    }

    [Fact]
    public void ValidateUpdate_ChangeStatusAndDueAtSimultaneously_ThrowsInvalidInputException()
    {
        // Arrange
        var reminder = new Reminder { Status = "pending" };

        // Act & Assert
        var ex = Assert.Throws<InvalidInputException>(() => 
            ReminderService.ValidateUpdate(reminder, "completed", DateTime.UtcNow.AddDays(1))
        );
        Assert.Contains("不允许在更改状态为 completed/cancelled 的同时修改 dueAt", ex.Message);
    }

    [Fact]
    public void ValidateUpdate_CompletedReminderModifyDueAt_ThrowsInvalidInputException()
    {
        // Arrange
        var reminder = new Reminder { Status = "completed" };

        // Act & Assert
        var ex = Assert.Throws<InvalidInputException>(() => 
            ReminderService.ValidateUpdate(reminder, null, DateTime.UtcNow.AddDays(1))
        );
        Assert.Contains("禁止修改 dueAt", ex.Message);
    }
}
