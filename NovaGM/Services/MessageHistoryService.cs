using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NovaGM.Models;

namespace NovaGM.Services
{
    /// <summary>
    /// Centralized message history service for conversation persistence
    /// </summary>
    public static class MessageHistoryService
    {
        private static readonly ConcurrentQueue<Message> _messageHistory = new();
        private static readonly object _lock = new();
        private static int _maxMessages = 500; // Prevent unlimited growth

        public static event Action<Message>? MessageAdded;

        public static void AddMessage(Message message)
        {
            lock (_lock)
            {
                _messageHistory.Enqueue(message);
                
                // Trim old messages if we exceed the limit
                while (_messageHistory.Count > _maxMessages)
                {
                    _messageHistory.TryDequeue(out _);
                }
            }
            
            MessageAdded?.Invoke(message);
        }

        public static List<Message> GetAllMessages()
        {
            lock (_lock)
            {
                return _messageHistory.ToList();
            }
        }

        public static List<Message> GetRecentMessages(int count = 50)
        {
            lock (_lock)
            {
                return _messageHistory.TakeLast(count).ToList();
            }
        }

        public static void ClearHistory()
        {
            lock (_lock)
            {
                _messageHistory.Clear();
            }
        }

        public static int GetMessageCount()
        {
            return _messageHistory.Count;
        }

        public static void SetMaxMessages(int maxMessages)
        {
            _maxMessages = Math.Max(10, maxMessages); // Minimum 10 messages
        }

        // Format messages for web client consumption
        public static object GetMessagesForWeb()
        {
            var messages = GetAllMessages();
            return messages.Select(m => new
            {
                role = m.Role,
                content = m.Content,
                timestamp = DateTime.Now.ToString("HH:mm:ss") // Could add actual timestamp to Message model
            }).ToArray();
        }
    }
}