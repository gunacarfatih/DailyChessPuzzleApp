
import 'package:flutter/material.dart';

import '../services/auth_api_service.dart';

class RegisterPage extends StatefulWidget {
  const RegisterPage({super.key});

  @override
  State<RegisterPage> createState() => _RegisterPageState();
}

class _RegisterPageState extends State<RegisterPage> {
  final AuthApiService _authService = AuthApiService();

  final GlobalKey<FormState> _formKey = GlobalKey<FormState>();

  final TextEditingController _usernameController =
  TextEditingController();

  final TextEditingController _emailController =
  TextEditingController();

  final TextEditingController _passwordController =
  TextEditingController();

  final TextEditingController _confirmPasswordController =
  TextEditingController();

  final TextEditingController _countryCodeController =
  TextEditingController();

  bool _isLoading = false;
  bool _obscurePassword = true;
  bool _obscureConfirmPassword = true;

  @override
  void dispose() {
    _usernameController.dispose();
    _emailController.dispose();
    _passwordController.dispose();
    _confirmPasswordController.dispose();
    _countryCodeController.dispose();
    super.dispose();
  }

  Future<void> _register() async {
    if (!_formKey.currentState!.validate()) {
      return;
    }

    setState(() {
      _isLoading = true;
    });

    try {
      final countryCode = _countryCodeController.text
          .trim()
          .toUpperCase();

      final response = await _authService.register(
        username: _usernameController.text,
        email: _emailController.text,
        password: _passwordController.text,
        countryCode:
        countryCode.isEmpty ? null : countryCode,
      );

      if (!mounted) return;

      if (response.success && response.data != null) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text(
              'Hesabın başarıyla oluşturuldu!',
            ),
            backgroundColor: Colors.green,
          ),
        );

        Navigator.pop(context, true);
      } else {
        _showError(
          response.message ?? 'Kayıt işlemi başarısız.',
        );
      }
    } catch (e) {
      if (!mounted) return;

      _showError(
        'Sunucuya bağlanırken bir hata oluştu. '
            'Lütfen tekrar deneyin.',
      );
    } finally {
      if (mounted) {
        setState(() {
          _isLoading = false;
        });
      }
    }
  }

  void _showError(String message) {
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(message),
        backgroundColor: Colors.red,
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Hesap Oluştur'),
      ),
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: Form(
              key: _formKey,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  const Icon(
                    Icons.person_add_alt_1,
                    size: 72,
                    color: Colors.amber,
                  ),

                  const SizedBox(height: 20),

                  const Text(
                    'Aramıza Katıl!',
                    textAlign: TextAlign.center,
                    style: TextStyle(
                      fontSize: 26,
                      fontWeight: FontWeight.bold,
                    ),
                  ),

                  const SizedBox(height: 8),

                  Text(
                    'Hesabını oluştur ve puanlarını kaybetmeden '
                        'satranç yolculuğuna devam et.',
                    textAlign: TextAlign.center,
                    style: TextStyle(
                      color: Colors.grey.shade400,
                    ),
                  ),

                  const SizedBox(height: 30),

                  TextFormField(
                    controller: _usernameController,
                    textCapitalization: TextCapitalization.none,
                    textInputAction: TextInputAction.next,
                    decoration: const InputDecoration(
                      labelText: 'Kullanıcı adı',
                      prefixIcon: Icon(Icons.person_outline),
                      border: OutlineInputBorder(),
                    ),
                    validator: (value) {
                      final username = value?.trim() ?? '';

                      if (username.isEmpty) {
                        return 'Kullanıcı adı gir.';
                      }

                      if (username.length < 3 ||
                          username.length > 50) {
                        return 'Kullanıcı adı 3-50 karakter olmalı.';
                      }

                      return null;
                    },
                  ),

                  const SizedBox(height: 16),

                  TextFormField(
                    controller: _emailController,
                    keyboardType: TextInputType.emailAddress,
                    textInputAction: TextInputAction.next,
                    decoration: const InputDecoration(
                      labelText: 'E-posta',
                      prefixIcon: Icon(Icons.email_outlined),
                      border: OutlineInputBorder(),
                    ),
                    validator: (value) {
                      final email = value?.trim() ?? '';

                      if (email.isEmpty) {
                        return 'E-posta adresi gir.';
                      }

                      if (!RegExp(
                        r'^[^@\s]+@[^@\s]+\.[^@\s]+$',
                      ).hasMatch(email)) {
                        return 'Geçerli bir e-posta gir.';
                      }

                      return null;
                    },
                  ),

                  const SizedBox(height: 16),

                  TextFormField(
                    controller: _countryCodeController,
                    textCapitalization: TextCapitalization.characters,
                    maxLength: 2,
                    textInputAction: TextInputAction.next,
                    decoration: const InputDecoration(
                      labelText: 'Ülke kodu (isteğe bağlı)',
                      hintText: 'TR',
                      prefixIcon: Icon(Icons.public),
                      border: OutlineInputBorder(),
                      counterText: '',
                    ),
                    validator: (value) {
                      final countryCode = value?.trim() ?? '';

                      if (countryCode.isNotEmpty &&
                          !RegExp(r'^[a-zA-Z]{2}$')
                              .hasMatch(countryCode)) {
                        return 'İki harfli ülke kodu gir. Örn: TR';
                      }

                      return null;
                    },
                  ),

                  const SizedBox(height: 16),

                  TextFormField(
                    controller: _passwordController,
                    obscureText: _obscurePassword,
                    textInputAction: TextInputAction.next,
                    decoration: InputDecoration(
                      labelText: 'Şifre',
                      prefixIcon: const Icon(Icons.lock_outline),
                      border: const OutlineInputBorder(),
                      suffixIcon: IconButton(
                        icon: Icon(
                          _obscurePassword
                              ? Icons.visibility_outlined
                              : Icons.visibility_off_outlined,
                        ),
                        onPressed: () {
                          setState(() {
                            _obscurePassword = !_obscurePassword;
                          });
                        },
                      ),
                    ),
                    validator: (value) {
                      if (value == null || value.isEmpty) {
                        return 'Şifre gir.';
                      }

                      if (value.length < 8 || value.length > 128) {
                        return 'Şifre 8-128 karakter olmalı.';
                      }

                      return null;
                    },
                  ),

                  const SizedBox(height: 16),

                  TextFormField(
                    controller: _confirmPasswordController,
                    obscureText: _obscureConfirmPassword,
                    textInputAction: TextInputAction.done,
                    onFieldSubmitted: (_) {
                      if (!_isLoading) _register();
                    },
                    decoration: InputDecoration(
                      labelText: 'Şifre tekrar',
                      prefixIcon: const Icon(Icons.lock_outline),
                      border: const OutlineInputBorder(),
                      suffixIcon: IconButton(
                        icon: Icon(
                          _obscureConfirmPassword
                              ? Icons.visibility_outlined
                              : Icons.visibility_off_outlined,
                        ),
                        onPressed: () {
                          setState(() {
                            _obscureConfirmPassword =
                            !_obscureConfirmPassword;
                          });
                        },
                      ),
                    ),
                    validator: (value) {
                      if (value == null || value.isEmpty) {
                        return 'Şifreni tekrar gir.';
                      }

                      if (value != _passwordController.text) {
                        return 'Şifreler eşleşmiyor.';
                      }

                      return null;
                    },
                  ),

                  const SizedBox(height: 26),

                  SizedBox(
                    height: 52,
                    child: ElevatedButton(
                      onPressed: _isLoading ? null : _register,
                      child: _isLoading
                          ? const SizedBox(
                        width: 22,
                        height: 22,
                        child: CircularProgressIndicator(
                          strokeWidth: 2,
                        ),
                      )
                          : const Text(
                        'Hesap Oluştur',
                        style: TextStyle(fontSize: 16),
                      ),
                    ),
                  ),

                  const SizedBox(height: 16),

                  Text(
                    'Kayıt olduğunda mevcut misafir puanların '
                        'hesabına aktarılır.',
                    textAlign: TextAlign.center,
                    style: TextStyle(
                      color: Colors.grey.shade400,
                      fontSize: 12,
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
