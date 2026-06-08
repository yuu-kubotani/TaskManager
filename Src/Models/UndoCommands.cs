using System;
using System.Collections.Generic;

namespace UniConsul.Models
{
    public interface IUndoCommand
    {
        void Undo();
    }

    public class TaskBulkDeleteCommand : IUndoCommand
    {
        private List<TaskItem> _deletedTasks;
        private Action<List<TaskItem>> _restoreAction;

        public TaskBulkDeleteCommand(List<TaskItem> deletedTasks, Action<List<TaskItem>> restoreAction)
        {
            _deletedTasks = deletedTasks;
            _restoreAction = restoreAction;
        }

        public void Undo()
        {
            if (_restoreAction != null) _restoreAction.Invoke(_deletedTasks);
        }
    }

    public class TaskStatusChangeCommand : IUndoCommand
    {
        private TaskItem _task;
        private string _oldStatus;
        private string _oldCompletionDate;
        private Action<TaskItem, string, string> _restoreAction;

        public TaskStatusChangeCommand(TaskItem task, string oldStatus, string oldCompletionDate, Action<TaskItem, string, string> restoreAction)
        {
            _task = task;
            _oldStatus = oldStatus;
            _oldCompletionDate = oldCompletionDate;
            _restoreAction = restoreAction;
        }

        public void Undo()
        {
            if (_restoreAction != null) _restoreAction.Invoke(_task, _oldStatus, _oldCompletionDate);
        }
    }
}
