
class ApiConstants {
  static const String baseUrl = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: 'http://192.168.1.34:5053',
  );

  // Daily Puzzle
  static const String todayPuzzleEndpoint = '/api/puzzle/today';

  static const String startPuzzleSessionEndpoint =
      '/api/puzzle/session/start';

  static const String submitPuzzleMoveEndpoint =
      '/api/puzzle/move';

  static const String usePuzzleHintEndpoint =
      '/api/puzzle/hint';

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
