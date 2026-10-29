using System;

namespace FieldInspection
{
    [Serializable] public class PositionData { public float x; public float y; public float z; }
    [Serializable] public class CreateIssue {
        public string title;
        public string description;
        public string priority;
        public PositionData position;
    }
    [Serializable] public class Issue : CreateIssue {
        public string id;
        public string status;
        public string createdAt;
        public string updatedAt;
    }
    [Serializable] public class ApiError { public ErrorDetail error; }
    [Serializable] public class ErrorDetail { public string code; public string message; }
}
