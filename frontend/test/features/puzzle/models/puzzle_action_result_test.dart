import 'package:flutter_test/flutter_test.dart';
import 'package:daily_chess_puzzle/features/puzzle/models/puzzle_action_result.dart';

void main() {
  test('parses successful move response and score', () {
    final result = PuzzleActionResult.fromJson({
      'playedMoves': ['e2e4', 'e7e5'],
      'currentMoveIndex': 2,
      'wrongMoves': 1,
      'usedHint': true,
      'isSolved': true,
      'isCompleted': true,
      'isExpired': false,
      'hintSquare': 'e4',
      'score': 580,
    });

    expect(result.playedMoves, ['e2e4', 'e7e5']);
    expect(result.currentMoveIndex, 2);
    expect(result.wrongMoves, 1);
    expect(result.usedHint, isTrue);
    expect(result.isSolved, isTrue);
    expect(result.isCompleted, isTrue);
    expect(result.isExpired, isFalse);
    expect(result.hintSquare, 'e4');
    expect(result.score, 580);
  });

  test('accepts PascalCase server properties and totalScore fallback', () {
    final result = PuzzleActionResult.fromJson({
      'PlayedMoves': ['g1f3'],
      'WrongMoves': 0,
      'TotalScore': 900,
    });

    expect(result.playedMoves, ['g1f3']);
    expect(result.wrongMoves, 0);
    expect(result.score, 900);
    expect(result.usedHint, isFalse);
  });
}
