
class AuthResponse {
  final int userId;
  final String username;
  final String email;
  final String? firstName;
  final String? lastName;
  final String? countryCode;
  final bool hasPassword;
  final String accessToken;
  final DateTime expiresAtUtc;

  const AuthResponse({
    required this.userId,
    required this.username,
    required this.email,
    this.firstName,
    this.lastName,
    this.countryCode,
    this.hasPassword = false,
    required this.accessToken,
    required this.expiresAtUtc,
  });

  factory AuthResponse.fromJson(Map<String, dynamic> json) {
    return AuthResponse(
      userId: (json['userId'] as num).toInt(),
      username: json['username']?.toString() ?? '',
      email: json['email']?.toString() ?? '',
      firstName: json['firstName']?.toString(),
      lastName: json['lastName']?.toString(),
      countryCode: json['countryCode']?.toString(),
      hasPassword: json['hasPassword'] == true,
      accessToken: json['accessToken']?.toString() ?? '',
      expiresAtUtc: DateTime.parse(
        json['expiresAtUtc'].toString(),
      ).toUtc(),
    );
  }
}
