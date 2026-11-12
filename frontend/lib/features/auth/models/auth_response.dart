
class AuthResponse {
  final int userId;
  final String username;
  final String email;
  final String? countryCode;
  final String accessToken;
  final DateTime expiresAtUtc;

  const AuthResponse({
    required this.userId,
    required this.username,
    required this.email,
    this.countryCode,
    required this.accessToken,
    required this.expiresAtUtc,
  });

  factory AuthResponse.fromJson(Map<String, dynamic> json) {
    return AuthResponse(
      userId: (json['userId'] as num).toInt(),
      username: json['username']?.toString() ?? '',
      email: json['email']?.toString() ?? '',
      countryCode: json['countryCode']?.toString(),
      accessToken: json['accessToken']?.toString() ?? '',
      expiresAtUtc: DateTime.parse(
        json['expiresAtUtc'].toString(),
      ).toUtc(),
    );
  }
}
