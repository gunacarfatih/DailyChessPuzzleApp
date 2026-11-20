import 'package:flutter_test/flutter_test.dart';
import 'package:daily_chess_puzzle/features/puzzle/models/puzzle_session.dart';

void main() {
  test('parses a resumed daily session', () {
    final session = PuzzleSession.fromJson({
      'sessionId': 42,
      'puzzleId': 9001,
      'playedMoves': ['e2e4'],
      'currentMoveIndex': 1,
      'wrongMoves': 2,
      'usedHint': false,
      'isSolved': false,
      'isCompleted': false,
      'startedAtUtc': '2026-11-20T10:00:00Z',
      'hintSquare': null,
      'score': null,
    });

    expect(session.sessionId, 42);
    expect(session.puzzleId, 9001);
    expect(session.playedMoves, ['e2e4']);
    expect(session.currentMoveIndex, 1);
    expect(session.wrongMoves, 2);
    expect(session.startedAtUtc, DateTime.utc(2026, 11, 20, 10));
  });
}
