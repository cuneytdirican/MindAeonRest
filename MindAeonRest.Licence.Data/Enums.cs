namespace MindAeonRest.Licence.Data;

/// <summary>Portal kullanıcı türü (rol ile birlikte).</summary>
public enum UserKind { Admin = 0, Dealer = 1, Customer = 2 }

/// <summary>Bayi tahsilat modeli — bayi bazında esnek (kullanıcı kararı).</summary>
public enum BillingMode { CustomerPaysUs = 0, DealerPaysUs = 1 }

/// <summary>Lisans durumu.</summary>
public enum LicenseStatus { Trial = 0, Active = 1, Expired = 2, Upgraded = 3, Revoked = 4 }

/// <summary>Lisans paket süresi.</summary>
public enum DurationKind { Perpetual = 0, Monthly = 1, Yearly = 2 }

/// <summary>Lisans geçmişi işlemi.</summary>
public enum LicenseAction { Issued = 0, Upgraded = 1, Renewed = 2, Revoked = 3 }

/// <summary>Talep durumu.</summary>
public enum RequestStatus { Pending = 0, Issued = 1, Rejected = 2 }

/// <summary>Ödeme sağlayıcısı (pluggable).</summary>
public enum PaymentProviderKind { Iyzico = 0, PayTR = 1, GarantiVPos = 2, BankTransfer = 3 }

/// <summary>Ödeme durumu.</summary>
public enum PaymentStatus { Pending = 0, Paid = 1, Failed = 2, Refunded = 3, Cancelled = 4 }

/// <summary>Destek talebi durumu.</summary>
public enum TicketStatus { Open = 0, InProgress = 1, Answered = 2, Closed = 3 }

/// <summary>Destek talebi önceliği.</summary>
public enum TicketPriority { Low = 0, Normal = 1, High = 2, Urgent = 3 }
