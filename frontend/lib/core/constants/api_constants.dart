class ApiConstants {
  static const String baseUrl = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: 'http://10.116.246.182:5053',
  );

  // Daily Puzzle
  static const String todayPuzzleEndpoint = '/api/puzzle/today';
  static const String submitPuzzleEndpoint = '/api/puzzle/submit';

  // Bonus Puzzle
  static const String bonusPuzzleEndpoint = '/api/puzzle/bonus';
  static const String startBonusPuzzleEndpoint =
      '/api/puzzle/bonus/start';
  static const String submitBonusMoveEndpoint =
      '/api/puzzle/bonus/move';
  static const String useBonusHintEndpoint =
      '/api/puzzle/bonus/hint';
  static const String submitBonusPuzzleEndpoint =
      '/api/puzzle/bonus/submit';

  // Leaderboard
  static const String leaderboardEndpoint = '/api/leaderboard';

  // Authentication
  static const String registerEndpoint = '/api/auth/register';
  static const String loginEndpoint = '/api/auth/login';
}