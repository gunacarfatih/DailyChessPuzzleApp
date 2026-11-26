import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../../providers/auth_provider.dart';

class PasswordResetPage extends StatefulWidget {
  const PasswordResetPage({super.key});

  @override
  State<PasswordResetPage> createState() => _PasswordResetPageState();
}

class _PasswordResetPageState extends State<PasswordResetPage> {
  final _email = TextEditingController();
  final _code = TextEditingController();
  final _password = TextEditingController();
  bool _codeRequested = false;

  @override
  void dispose() {
    _email.dispose();
    _code.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _requestCode() async {
    final result = await context.read<AuthProvider>().requestPasswordReset(
          email: _email.text,
        );
    if (!mounted) return;
    if (!result.success) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(result.message ?? 'İstek gönderilemedi.')),
      );
      return;
    }
    setState(() => _codeRequested = true);
    ScaffoldMessenger.of(context).showSnackBar(
      const SnackBar(content: Text('Hesap varsa sıfırlama kodu e-postayla gönderildi.')),
    );
  }

  Future<void> _resetPassword() async {
    final result = await context.read<AuthProvider>().resetPassword(
          email: _email.text,
          code: _code.text,
          newPassword: _password.text,
        );
    if (!mounted) return;
    if (result.success) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Şifreniz güncellendi. Giriş yapabilirsiniz.')),
      );
      Navigator.pop(context);
    } else {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(result.message ?? 'Şifre sıfırlanamadı.')),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final isLoading = context.watch<AuthProvider>().isLoading;
    return Scaffold(
      appBar: AppBar(title: const Text('Şifremi Unuttum')),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.all(24),
          children: [
            TextField(
              controller: _email,
              keyboardType: TextInputType.emailAddress,
              decoration: const InputDecoration(labelText: 'E-posta'),
            ),
            if (_codeRequested) ...[
              const SizedBox(height: 16),
              TextField(
                controller: _code,
                keyboardType: TextInputType.number,
                maxLength: 6,
                decoration: const InputDecoration(labelText: 'E-postadaki 6 haneli kod'),
              ),
              TextField(
                controller: _password,
                obscureText: true,
                decoration: const InputDecoration(labelText: 'Yeni şifre'),
              ),
            ],
            const SizedBox(height: 20),
            FilledButton(
              onPressed: isLoading
                  ? null
                  : (_codeRequested ? _resetPassword : _requestCode),
              child: Text(isLoading
                  ? 'Bekleyin...'
                  : (_codeRequested ? 'Şifreyi Güncelle' : 'Sıfırlama Kodu Gönder')),
            ),
          ],
        ),
      ),
    );
  }
}
