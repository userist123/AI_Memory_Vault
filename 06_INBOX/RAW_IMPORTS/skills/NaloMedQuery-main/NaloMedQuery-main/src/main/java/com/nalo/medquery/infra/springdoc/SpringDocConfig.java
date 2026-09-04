package com.nalo.medquery.infra.springdoc;

import org.springframework.context.annotation.Bean;

import io.swagger.v3.oas.models.Components;
import io.swagger.v3.oas.models.OpenAPI;
import io.swagger.v3.oas.models.info.Contact;
import io.swagger.v3.oas.models.info.Info;
import io.swagger.v3.oas.models.security.SecurityRequirement;
import io.swagger.v3.oas.models.security.SecurityScheme;
import org.springframework.context.annotation.Configuration;

@Configuration
public class SpringDocConfig {
    @Bean
    public OpenAPI customOpenAPI() {
    return new OpenAPI()
            .components(new Components()
                .addSecuritySchemes("bearer-key",
                    new SecurityScheme()
                        .type(SecurityScheme.Type.HTTP)
                        .scheme("bearer")
                        .bearerFormat("JWT")
                )
            )
            .addSecurityItem(new SecurityRequirement().addList("bearer-key"))
            .info(new Info()
                .title("Nalo medQuery API")
                .description(
                    "API Rest da aplicação MedQuery, contendo as funcionalidades de " +
                    "CRUD de médicos e pacientes, além de agendamento e cancelamento de consultas"
                )
                .contact(new Contact()
                    .name("NaloNetworks")
                )
            );
    }
}
