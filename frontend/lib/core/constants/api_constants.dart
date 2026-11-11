class ApiConstants {
  static const String baseUrl = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: 'http://10.0.2.2:5053',
  );

  static const String todayPuzzleEndpoint = '/api/puzzle/today';
  static const String submitPuzzleEndpoint = '/api/puzzle/submit';
  static const String leaderboardEndpoint = '/api/leaderboard';
}