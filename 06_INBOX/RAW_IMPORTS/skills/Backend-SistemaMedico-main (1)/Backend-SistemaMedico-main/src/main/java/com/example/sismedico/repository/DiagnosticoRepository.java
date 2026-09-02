package com.example.sismedico.repository;

import com.example.sismedico.entity.Cita;
import com.example.sismedico.entity.Diagnostico;
import org.springframework.data.jpa.repository.JpaRepository;

import java.util.Optional;

public interface DiagnosticoRepository extends JpaRepository<Diagnostico, Long> {

    Optional<Diagnostico> findByCita(Cita cita);

    boolean existsByCita(Cita cita);

}