namespace MestoBeach.Sessions.Domain.Enums;

public enum SessionStatus { Open, Closed }

public enum QrMode { Fixed, Dynamic }

public enum ServiceMode { Staffed, SelfService }

public enum ClosureReason { PaymentCompleted, ManualClose, SystemTimeout }
